using EFiling.Nop.Domain;
using Microsoft.Extensions.Logging;
using Nop.Core;
using Nop.Core.Domain.Messages;
using Nop.Services.Customers;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Stores;

namespace EFiling.Nop.Services;

/// <summary>
/// Sends email notifications to customers when their court filing status changes.
/// Uses nopCommerce's message template and email queue infrastructure.
///
/// Message templates must be created in nopCommerce Admin → Message Templates:
///   - EFiling.FilingAccepted.Customer
///   - EFiling.FilingRejected.Customer
///   - EFiling.FilingPartiallyAccepted.Customer
///
/// Available tokens in templates:
///   %EFiling.OrderNumber%, %EFiling.FilingStatus%, %EFiling.CourtName%,
///   %EFiling.CaseNumber%, %EFiling.CaseTitle%, %EFiling.FilingType%,
///   %EFiling.ErrorText%, %EFiling.OrderDetailsUrl%,
///   %Customer.FullName%, %Customer.Email%, %Store.Name%
/// </summary>
public class EFilingNotificationService : IEFilingNotificationService
{
    // Message template system names — must match DB entries
    public const string FILING_ACCEPTED_CUSTOMER = "EFiling.FilingAccepted.Customer";
    public const string FILING_REJECTED_CUSTOMER = "EFiling.FilingRejected.Customer";
    public const string FILING_PARTIALLY_ACCEPTED_CUSTOMER = "EFiling.FilingPartiallyAccepted.Customer";

    private readonly IWorkflowMessageService _workflowMessageService;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly IOrderService _orderService;
    private readonly ICustomerService _customerService;
    private readonly IStoreService _storeService;
    private readonly IStoreContext _storeContext;
    private readonly IEmailAccountService _emailAccountService;
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly ILogger<EFilingNotificationService> _logger;

    public EFilingNotificationService(
        IWorkflowMessageService workflowMessageService,
        IMessageTemplateService messageTemplateService,
        IOrderService orderService,
        ICustomerService customerService,
        IStoreService storeService,
        IStoreContext storeContext,
        IEmailAccountService emailAccountService,
        EmailAccountSettings emailAccountSettings,
        ILogger<EFilingNotificationService> logger)
    {
        _workflowMessageService = workflowMessageService;
        _messageTemplateService = messageTemplateService;
        _orderService = orderService;
        _customerService = customerService;
        _storeService = storeService;
        _storeContext = storeContext;
        _emailAccountService = emailAccountService;
        _emailAccountSettings = emailAccountSettings;
        _logger = logger;
    }

    public async Task SendFilingStatusChangedAsync(
        EFilingOrderRecord orderRecord, string? previousStatus, CancellationToken ct = default)
    {
        // Don't send if status hasn't actually changed
        if (string.Equals(orderRecord.FilingStatus, previousStatus, StringComparison.OrdinalIgnoreCase))
            return;

        // Determine which template to use
        var templateName = orderRecord.FilingStatus?.ToUpperInvariant() switch
        {
            "ACCEPTED" or "REVIEWED" => FILING_ACCEPTED_CUSTOMER,
            "REJECTED" or "CANCELLED" => FILING_REJECTED_CUSTOMER,
            "PARTIALLY_ACCEPTED" or "PARTIALLYACCEPTED" => FILING_PARTIALLY_ACCEPTED_CUSTOMER,
            _ => null
        };

        if (templateName == null)
            return; // No notification for RECEIVED_UNDER_REVIEW or unknown statuses

        try
        {
            var order = await _orderService.GetOrderByIdAsync(orderRecord.OrderId);
            if (order == null) return;

            var store = await _storeService.GetStoreByIdAsync(order.StoreId)
                     ?? await _storeContext.GetCurrentStoreAsync();

            // Look up message template
            var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync(templateName, store.Id);
            var template = templates.FirstOrDefault(t => t.IsActive);
            if (template == null)
            {
                _logger.LogDebug("Message template '{Template}' not found or inactive — skipping notification for filing {Id}",
                    templateName, orderRecord.Id);
                return;
            }

            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
            if (customer == null) return;

            var customerName = await _customerService.GetCustomerFullNameAsync(customer);

            // Build filing-specific tokens
            var statusLabel = orderRecord.FilingStatus?.ToUpperInvariant() switch
            {
                "ACCEPTED" or "REVIEWED" => "Accepted",
                "REJECTED" or "CANCELLED" => "Rejected",
                "PARTIALLY_ACCEPTED" or "PARTIALLYACCEPTED" => "Partially Accepted",
                _ => orderRecord.FilingStatus ?? "Unknown"
            };

            var orderDetailsUrl = $"{store.Url.TrimEnd('/')}/orderdetails/{order.Id}";

            var tokens = new List<Token>
            {
                new("EFiling.OrderNumber", order.CustomOrderNumber ?? order.Id.ToString()),
                new("EFiling.FilingStatus", statusLabel),
                new("EFiling.CourtName", orderRecord.CourtId ?? ""),
                new("EFiling.CaseNumber", orderRecord.CaseNumber ?? "Pending"),
                new("EFiling.CaseTitle", orderRecord.CaseTitle ?? ""),
                new("EFiling.FilingType", orderRecord.FilingType ?? ""),
                new("EFiling.ErrorText", orderRecord.ErrorText ?? ""),
                new("EFiling.OrderDetailsUrl", orderDetailsUrl),
                new("Customer.FullName", customerName ?? ""),
                new("Customer.Email", customer.Email ?? ""),
                new("Store.Name", store.Name ?? ""),
            };

            // Get email account
            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(
                template.EmailAccountId != 0 ? template.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId);
            emailAccount ??= (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();

            if (emailAccount == null)
            {
                _logger.LogWarning("No email account configured — cannot send filing notification for {Id}", orderRecord.Id);
                return;
            }

            // Send to all notification recipients (filer + additional emails stored on the order record)
            var recipients = (orderRecord.NotificationEmails ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Fallback: if no notification emails stored, use customer email
            if (recipients.Count == 0 && !string.IsNullOrEmpty(customer.Email))
                recipients.Add(customer.Email);

            foreach (var recipientEmail in recipients)
            {
                await _workflowMessageService.SendNotificationAsync(
                    template, emailAccount, 0, tokens,
                    recipientEmail, recipientEmail);
            }

            _logger.LogInformation(
                "Filing status notification '{Template}' queued for order {OrderId} (filing {FilingId}, recipients={Recipients})",
                templateName, order.Id, orderRecord.Id, string.Join(";", recipients));
        }
        catch (Exception ex)
        {
            // Non-fatal — don't let email failures break the filing status flow
            _logger.LogWarning(ex, "Failed to send filing notification for order record {Id}", orderRecord.Id);
        }
    }
}

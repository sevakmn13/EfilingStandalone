using FluentMigrator;
using Nop.Data.Migrations;

namespace EFiling.Nop.Data.Migrations;

/// <summary>
/// Seeds a "Court Filing Service" product into nopCommerce's Product table.
/// SKU = COURT-FILING-SVC, CustomerEntersPrice = true.
/// Used by SubmitAndPayAjax to add the court fee amount as a cart item.
/// </summary>
[NopMigration("2026/03/19 00:01:00", "EFiling. Seed Court Filing Service product", MigrationProcessType.Update)]
public class SeedCourtFilingProduct : Migration
{
    public override void Up()
    {
        // Only insert if a product with this SKU doesn't already exist
        Execute.Sql("""
            IF NOT EXISTS (SELECT 1 FROM Product WHERE Sku = 'COURT-FILING-SVC' AND Deleted = 0)
            BEGIN
                INSERT INTO Product
                    (ProductTypeId, ParentGroupedProductId, VisibleIndividually,
                     Name, ShortDescription, FullDescription, AdminComment,
                     Sku, Published, Deleted,
                     Price, OldPrice, ProductCost,
                     CustomerEntersPrice, MinimumCustomerEnteredPrice, MaximumCustomerEnteredPrice,
                     IsGiftCard, GiftCardTypeId,
                     IsDownload, DownloadId, UnlimitedDownloads, MaxNumberOfDownloads, DownloadExpirationDays,
                     DownloadActivationTypeId, HasSampleDownload, SampleDownloadId,
                     HasUserAgreement,
                     IsRecurring, RecurringCycleLength, RecurringCyclePeriodId, RecurringTotalCycles,
                     IsRental, RentalPriceLength, RentalPricePeriodId,
                     IsShipEnabled, IsFreeShipping, ShipSeparately, AdditionalShippingCharge,
                     DeliveryDateId, IsTaxExempt, TaxCategoryId,
                     IsTelecommunicationsOrBroadcastingOrElectronicServices,
                     ManageInventoryMethodId, ProductAvailabilityRangeId,
                     UseMultipleWarehouses, WarehouseId,
                     StockQuantity, DisplayStockAvailability, DisplayStockQuantity,
                     MinStockQuantity, LowStockActivityId, NotifyAdminForQuantityBelow, BackorderModeId,
                     AllowBackInStockSubscriptions, OrderMinimumQuantity, OrderMaximumQuantity,
                     AllowAddingOnlyExistingAttributeCombinations,
                     NotReturnable, DisableBuyButton, DisableWishlistButton,
                     AvailableForPreOrder,
                     CallForPrice, MarkAsNew,
                     HasTierPrices, HasDiscountsApplied,
                     BasepriceEnabled, BasepriceAmount, BasepriceUnitId, BasepriceBaseAmount, BasepriceBaseUnitId,
                     SubjectToAcl, LimitedToStores,
                     DisplayOrder, CreatedOnUtc, UpdatedOnUtc)
                VALUES
                    (5 /* SimpleProduct */, 0, 0 /* hidden from catalog */,
                     'Court Filing Service', 'Court e-filing fees', '', '',
                     'COURT-FILING-SVC', 1, 0,
                     0, 0, 0,
                     1 /* CustomerEntersPrice */, 0, 100000.00,
                     0, 0,
                     0, 0, 0, 0, NULL,
                     0, 0, 0,
                     0,
                     0, 0, 0, 0,
                     0, 0, 0,
                     0 /* no shipping */, 0, 0, 0,
                     0, 1 /* tax exempt */, 0,
                     0,
                     0 /* no inventory tracking */, 0,
                     0, 0,
                     10000, 0, 0,
                     0, 0, 0, 0,
                     0, 1, 1,
                     0,
                     1 /* not returnable */, 0, 1 /* no wishlist */,
                     0,
                     0, 0,
                     0, 0,
                     0, 0, 0, 0, 0,
                     0, 0,
                     0, GETUTCDATE(), GETUTCDATE());
            END
            """);
    }

    public override void Down()
    {
        Execute.Sql("UPDATE Product SET Deleted = 1 WHERE Sku = 'COURT-FILING-SVC'");
    }
}

using EFiling.Core.Caching;
using EFiling.Providers.JTI;
using Xunit;
using Xunit.Abstractions;

namespace EFiling.Tests;

/// <summary>
/// Investigation tests to understand document filtering discrepancy.
/// Case: Madera court, caseTypeCode=411110, caseCategoryCode=401100 (Auto Tort 22)
/// Current filter: 256 docs, Competitor shows: ~296 docs
/// </summary>
public class DocumentFilterInvestigationTests
{
    private readonly ITestOutputHelper _output;
    
    public DocumentFilterInvestigationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SearchForMissingDocs()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        
        // Get policy to see the document list URL
        var policy = await provider.GetPolicyAsync(config);
        _output.WriteLine($"Document List URL: {policy.DocumentListUrl}");
        _output.WriteLine($"REST Base URL: {config.RestBaseUrl}");
        _output.WriteLine("");
        
        // Get ALL documents (no filters)
        var allDocs = await provider.GetDocumentListAsync(config, caseType: null, subFiling: true);
        _output.WriteLine($"Total docs from subFiling=true: {allDocs.Count}");
        
        // Analyze the 37K - why so many?
        var uniqueByCodes = allDocs.GroupBy(d => d.Code).ToList();
        _output.WriteLine($"Unique by Code: {uniqueByCodes.Count}");
        
        var uniqueByName = allDocs.Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .GroupBy(d => d.Name).ToList();
        _output.WriteLine($"Unique by Name (non-empty): {uniqueByName.Count}");
        
        // How many have empty names?
        var emptyNames = allDocs.Count(d => string.IsNullOrWhiteSpace(d.Name));
        _output.WriteLine($"Docs with EMPTY names: {emptyNames}");
        
        // Sample of docs with empty names
        var emptyNameSamples = allDocs.Where(d => string.IsNullOrWhiteSpace(d.Name)).Take(5).ToList();
        _output.WriteLine("Sample empty-name docs:");
        foreach (var d in emptyNameSamples)
        {
            _output.WriteLine($"  Code: {d.Code}, CaseTypes: [{string.Join(",", d.CaseTypes.Take(3))}]");
        }
        
        // Are there duplicate codes with same data, or different data?
        var duplicateCodes = uniqueByCodes.Where(g => g.Count() > 1).Take(5).ToList();
        _output.WriteLine($"\nDuplicate codes (same code, multiple entries): {uniqueByCodes.Count(g => g.Count() > 1)}");
        foreach (var g in duplicateCodes)
        {
            _output.WriteLine($"  Code '{g.Key}' appears {g.Count()} times");
            foreach (var d in g.Take(2))
            {
                _output.WriteLine($"    Name: {d.Name}, CaseTypes: [{string.Join(",", d.CaseTypes.Take(3))}]");
            }
        }
        
        var allDocsNoSub = await provider.GetDocumentListAsync(config, caseType: null, subFiling: false);
        _output.WriteLine($"\nTotal docs from subFiling=false: {allDocsNoSub.Count}");
        
        // Combine both
        var combinedDocs = allDocs.Concat(allDocsNoSub)
            .GroupBy(d => d.Code)
            .Select(g => g.First())
            .ToList();
        _output.WriteLine($"Total unique docs combined: {combinedDocs.Count}");
        
        // Search for specific missing doc names (partial match)
        var searchTerms = new[] { 
            "Exhibit", "Statement", "Proof", "Assignment", "Resignation",
            "Stipulation", "Waiver", "Return", "Transcription", "Nomination"
        };
        
        _output.WriteLine("");
        _output.WriteLine("=== SEARCHING FOR TERMS IN ALL DOCS ===");
        foreach (var term in searchTerms)
        {
            var matches = combinedDocs.Where(d => 
                d.Name?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            _output.WriteLine($"\n'{term}' found in {matches.Count} docs:");
            foreach (var m in matches.Take(10))
            {
                _output.WriteLine($"  - {m.Code}: {m.Name}");
                _output.WriteLine($"      CaseTypes: [{string.Join(",", m.CaseTypes.Take(3))}{(m.CaseTypes.Count > 3 ? "..." : "")}]");
            }
        }
    }

    [Fact]
    public async Task ListCivilUnlimitedCodes()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        
        _output.WriteLine("=== CASE_TYPE codes (Civil Unlimited) ===");
        var caseTypes = await provider.GetCodeListAsync(config, "CASE_TYPE");
        var civilTypes = caseTypes
            .Where(c => c.Name?.Contains("Civil", StringComparison.OrdinalIgnoreCase) == true ||
                       c.Name?.Contains("Tort", StringComparison.OrdinalIgnoreCase) == true ||
                       c.Name?.Contains("Auto", StringComparison.OrdinalIgnoreCase) == true ||
                       c.Code?.StartsWith("4") == true) // Civil codes often start with 4
            .OrderBy(c => c.Code)
            .ToList();
        
        foreach (var ct in civilTypes)
        {
            _output.WriteLine($"  {ct.Code}: {ct.Name}");
        }
        
        _output.WriteLine("");
        _output.WriteLine("=== CASE_CATEGORY codes (Civil/Auto Tort) ===");
        var categories = await provider.GetCodeListAsync(config, "CASE_CATEGORY");
        var civilCats = categories
            .Where(c => c.Name?.Contains("Civil", StringComparison.OrdinalIgnoreCase) == true ||
                       c.Name?.Contains("Tort", StringComparison.OrdinalIgnoreCase) == true ||
                       c.Name?.Contains("Auto", StringComparison.OrdinalIgnoreCase) == true ||
                       c.Code?.StartsWith("4") == true)
            .OrderBy(c => c.Code)
            .ToList();
        
        foreach (var cat in civilCats)
        {
            _output.WriteLine($"  {cat.Code}: {cat.Name}");
            // Show related CASE_TYPE codes
            var relatedTypes = cat.Relationships
                .Where(r => r.RelatedListName == "CASE_TYPE")
                .Select(r => r.RelatedCode)
                .ToList();
            if (relatedTypes.Count > 0)
            {
                _output.WriteLine($"    -> Related CASE_TYPEs: [{string.Join(", ", relatedTypes)}]");
            }
        }
        
        _output.WriteLine("");
        _output.WriteLine("=== Looking specifically for 'Auto Tort' ===");
        var autoTort = categories.FirstOrDefault(c => 
            c.Name?.Contains("Auto Tort", StringComparison.OrdinalIgnoreCase) == true);
        if (autoTort != null)
        {
            _output.WriteLine($"Found: {autoTort.Code} = {autoTort.Name}");
            var relTypes = autoTort.Relationships
                .Where(r => r.RelatedListName == "CASE_TYPE")
                .Select(r => r.RelatedCode)
                .ToList();
            _output.WriteLine($"Related CASE_TYPEs: [{string.Join(", ", relTypes)}]");
        }
        else
        {
            _output.WriteLine("No 'Auto Tort' category found - listing all categories:");
            foreach (var cat in categories.OrderBy(c => c.Code))
            {
                _output.WriteLine($"  {cat.Code}: {cat.Name}");
            }
        }
    }

    [Fact]
    public async Task AnalyzeLiveDocumentList()
    {
        // Use TestConfiguration which handles credential decryption
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        
        // Fetch documents WITHOUT caseType filter (like competitor might do)
        var docsNoFilter = await provider.GetDocumentListAsync(config, caseType: null, subFiling: true);
        _output.WriteLine($"Documents from API (NO caseType filter): {docsNoFilter.Count}");
        
        // Fetch documents WITH caseType filter
        var docsWithFilter = await provider.GetDocumentListAsync(config, "411110", subFiling: true);
        _output.WriteLine($"Documents from API (WITH caseType=411110): {docsWithFilter.Count}");
        
        // Use unfiltered docs for analysis
        var docs = docsNoFilter;
        _output.WriteLine($"Using unfiltered docs for analysis: {docs.Count}");
        
        // Fetch CASE_CATEGORY to get all related CASE_TYPE codes
        var categories = await provider.GetCodeListAsync(config, "CASE_CATEGORY");
        var category = categories.FirstOrDefault(c => c.Code == "401100");
        
        var allCodes = new HashSet<string> { "411110", "401100" };
        if (category != null)
        {
            _output.WriteLine($"Found category 401100: {category.Name}");
            foreach (var rel in category.Relationships.Where(r => r.RelatedListName == "CASE_TYPE"))
            {
                allCodes.Add(rel.RelatedCode);
                _output.WriteLine($"  Related CASE_TYPE: {rel.RelatedCode}");
            }
        }
        _output.WriteLine($"All target codes: [{string.Join(", ", allCodes)}]");
        _output.WriteLine("");
        
        // Analyze document distribution
        var withCaseTypes = docs.Count(d => d.CaseTypes.Count > 0);
        var withCaseCategories = docs.Count(d => d.CaseCategories.Count > 0);
        var withBoth = docs.Count(d => d.CaseTypes.Count > 0 && d.CaseCategories.Count > 0);
        var withNeither = docs.Count(d => d.CaseTypes.Count == 0 && d.CaseCategories.Count == 0);
        
        _output.WriteLine("=== Document Distribution ===");
        _output.WriteLine($"With CaseTypes: {withCaseTypes}");
        _output.WriteLine($"With CaseCategories: {withCaseCategories}");
        _output.WriteLine($"With both: {withBoth}");
        _output.WriteLine($"With neither (unrestricted): {withNeither}");
        _output.WriteLine("");
        
        // Get documents matching our codes
        var matching = docs.Where(d => 
            d.CaseTypes.Any(t => allCodes.Contains(t)) || 
            d.CaseCategories.Any(c => allCodes.Contains(c))).ToList();
        
        _output.WriteLine($"=== Matching Target Codes ===");
        _output.WriteLine($"Matching CaseTypes: {docs.Count(d => d.CaseTypes.Any(t => allCodes.Contains(t)))}");
        _output.WriteLine($"Matching CaseCategories: {docs.Count(d => d.CaseCategories.Any(c => allCodes.Contains(c)))}");
        _output.WriteLine($"Matching either: {matching.Count}");
        _output.WriteLine("");
        
        // FormGroup analysis for matching docs
        _output.WriteLine("=== FormGroup Distribution (matching docs) ===");
        var formGroupCombos = matching
            .GroupBy(d => string.Join("+", d.FormGroups.OrderBy(f => f)))
            .OrderByDescending(g => g.Count())
            .Take(25);
        foreach (var g in formGroupCombos)
        {
            var key = string.IsNullOrEmpty(g.Key) ? "(none)" : g.Key;
            _output.WriteLine($"  {key}: {g.Count()}");
        }
        _output.WriteLine("");
        
        // Filtering scenarios
        _output.WriteLine("=== Filtering Scenarios ===");
        
        var excludeEfciLeadOnly = matching.Count(d => 
            !(d.FormGroups.Contains("EFCI_LEAD") && !d.FormGroups.Contains("EF_LEAD")));
        _output.WriteLine($"1. Exclude EFCI_LEAD only (current): {excludeEfciLeadOnly}");
        
        var efLeadOnly = matching.Count(d => d.FormGroups.Contains("EF_LEAD"));
        _output.WriteLine($"2. EF_LEAD only: {efLeadOnly}");
        
        var efLeadPlusEfne = matching.Count(d => 
            d.FormGroups.Contains("EF_LEAD") || d.FormGroups.Contains("EFNE"));
        _output.WriteLine($"3. EF_LEAD + EFNE: {efLeadPlusEfne}");
        
        var efLeadPlusEfnePlusEfci = matching.Count(d => 
            d.FormGroups.Contains("EF_LEAD") || d.FormGroups.Contains("EFNE") || d.FormGroups.Contains("EFCI"));
        _output.WriteLine($"4. EF_LEAD + EFNE + EFCI: {efLeadPlusEfnePlusEfci}");
        
        var anyWithFormGroups = matching.Count(d => d.FormGroups.Count > 0);
        _output.WriteLine($"5. Any with FormGroups: {anyWithFormGroups}");
        
        var allMatching = matching.Count;
        _output.WriteLine($"6. All matching (incl empty FormGroups): {allMatching}");
        
        // Check if there's a pattern we're missing - look at documents NOT in our filter
        var notInCurrentFilter = matching.Where(d => 
            d.FormGroups.Contains("EFCI_LEAD") && !d.FormGroups.Contains("EF_LEAD")).ToList();
        _output.WriteLine("");
        _output.WriteLine($"=== Excluded by current filter (EFCI_LEAD only): {notInCurrentFilter.Count} ===");
        foreach (var doc in notInCurrentFilter.Take(10))
        {
            _output.WriteLine($"  {doc.Code}: {doc.Name}");
            _output.WriteLine($"    FormGroups: [{string.Join(", ", doc.FormGroups)}]");
        }
        
        // What about FG_NAME_EXT or other form groups?
        var withFgNameExt = matching.Where(d => d.FormGroups.Contains("FG_NAME_EXT")).ToList();
        _output.WriteLine("");
        _output.WriteLine($"=== With FG_NAME_EXT: {withFgNameExt.Count} ===");
        
        var withFgSubLead = matching.Where(d => d.FormGroups.Contains("FG_SUB_LEAD")).ToList();
        _output.WriteLine($"=== With FG_SUB_LEAD: {withFgSubLead.Count} ===");
        
        // Sample the "missing" ~40 docs - docs that competitor shows but we don't
        // These would be docs with FormGroups we're not including
        var potentiallyMissing = matching.Where(d => 
            d.FormGroups.Count > 0 &&
            !d.FormGroups.Contains("EF_LEAD") &&
            !d.FormGroups.Contains("EFNE") &&
            !(d.FormGroups.Contains("EFCI_LEAD") && !d.FormGroups.Contains("EF_LEAD"))).ToList();
        _output.WriteLine("");
        _output.WriteLine($"=== Potentially missing (has FormGroups, not EF_LEAD/EFNE, not EFCI_LEAD-only): {potentiallyMissing.Count} ===");
        foreach (var doc in potentiallyMissing.Take(15))
        {
            _output.WriteLine($"  {doc.Code}: {doc.Name}");
            _output.WriteLine($"    FormGroups: [{string.Join(", ", doc.FormGroups)}]");
        }
        
        // INVESTIGATE: Documents that have BOTH EFCI_LEAD and EFCI (would appear in both dropdowns)
        _output.WriteLine("");
        _output.WriteLine("=== BUG INVESTIGATION: Docs with BOTH EFCI_LEAD and EFCI ===");
        var docsWithBothLeadAndCi = docs.Where(d => 
            d.FormGroups.Contains("EFCI_LEAD") && d.FormGroups.Contains("EFCI")).ToList();
        _output.WriteLine($"Count: {docsWithBothLeadAndCi.Count}");
        foreach (var doc in docsWithBothLeadAndCi.Take(20))
        {
            _output.WriteLine($"  {doc.Code}: {doc.Name}");
            _output.WriteLine($"    FormGroups: [{string.Join(", ", doc.FormGroups)}]");
        }
        
        // Specifically look for Complaint and Petition
        _output.WriteLine("");
        _output.WriteLine("=== Specific: Complaint and Petition docs ===");
        var complaintPetition = docs.Where(d => 
            d.Name.Contains("Complaint", StringComparison.OrdinalIgnoreCase) || 
            d.Name.Contains("Petition", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var doc in complaintPetition.Take(30))
        {
            _output.WriteLine($"  {doc.Code}: {doc.Name}");
            _output.WriteLine($"    FormGroups: [{string.Join(", ", doc.FormGroups)}]");
        }
        
        // SUBSEQUENT FILING ANALYSIS
        _output.WriteLine("");
        _output.WriteLine("========================================");
        _output.WriteLine("=== SUBSEQUENT FILING DOCUMENT TYPES ===");
        _output.WriteLine("========================================");
        
        // IMPORTANT: For subsequent filing, competitor may NOT filter by case type!
        // Let's compare filtered vs unfiltered counts
        
        _output.WriteLine("");
        _output.WriteLine("=== COMPARISON: Case-Type Filtered vs ALL Documents ===");
        
        // ALL docs (no case type filter) with subsequent filing form groups
        var allSubLeadDocs = docs.Where(d => d.FormGroups.Contains("EF_LEAD")).ToList();
        var allSubAdditionalDocs = docs.Where(d => 
            d.FormGroups.Contains("EFCI") || d.FormGroups.Contains("EF_LEAD") || d.FormGroups.Contains("EFNE")).ToList();
        var allWithAnyFG = docs.Where(d => d.FormGroups.Count > 0).ToList();
        
        // Case-type filtered
        var filteredSubLeadDocs = matching.Where(d => d.FormGroups.Contains("EF_LEAD")).ToList();
        var filteredSubAdditionalDocs = matching.Where(d => 
            d.FormGroups.Contains("EFCI") || d.FormGroups.Contains("EF_LEAD") || d.FormGroups.Contains("EFNE")).ToList();
        var filteredWithAnyFG = matching.Where(d => d.FormGroups.Count > 0).ToList();
        
        _output.WriteLine($"                              | ALL DOCS | FILTERED (Auto Tort 22)");
        _output.WriteLine($"  Lead (EF_LEAD)              |  {allSubLeadDocs.Count,6}  |  {filteredSubLeadDocs.Count,6}");
        _output.WriteLine($"  Additional (EFCI|EF_LEAD|EFNE)  |  {allSubAdditionalDocs.Count,6}  |  {filteredSubAdditionalDocs.Count,6}");
        _output.WriteLine($"  Any FormGroup               |  {allWithAnyFG.Count,6}  |  {filteredWithAnyFG.Count,6}");
        
        // MATCH EXACT SubsequentFiling.cshtml LOGIC:
        // Include if: (unrestricted with non-empty name) OR (matches case type)
        var subsequentFilingDocs = docs.Where(d => {
            var types = d.CaseTypes ?? new List<string>();
            var cats = d.CaseCategories ?? new List<string>();
            var isUnrestricted = types.Count == 0 && cats.Count == 0;
            
            if (isUnrestricted) {
                // Unrestricted: include if has name
                return !string.IsNullOrWhiteSpace(d.Name);
            }
            
            // Has restrictions: check for case type match
            return allCodes.Any(tc => types.Contains(tc) || cats.Contains(tc));
        }).OrderBy(d => d.Name).ToList();
        
        _output.WriteLine("");
        _output.WriteLine("=============================================");
        _output.WriteLine("=== MATCHING SubsequentFiling.cshtml LOGIC ===");
        _output.WriteLine("=============================================");
        _output.WriteLine($"Total docs following SubsequentFiling logic: {subsequentFilingDocs.Count}");
        _output.WriteLine("  (unrestricted with name) + (case-type matching)");
        
        // Break down by type
        var unrestrictedCount = subsequentFilingDocs.Count(d => 
            (d.CaseTypes?.Count ?? 0) == 0 && (d.CaseCategories?.Count ?? 0) == 0);
        var caseSpecificCount = subsequentFilingDocs.Count - unrestrictedCount;
        _output.WriteLine($"  Unrestricted: {unrestrictedCount}");
        _output.WriteLine($"  Case-specific: {caseSpecificCount}");
        
        // Lead docs (canBeLeadDoc): empty FormGroups OR has EF_LEAD
        var leadDocs = subsequentFilingDocs.Where(d => 
            d.FormGroups.Count == 0 || d.FormGroups.Contains("EF_LEAD")).ToList();
        _output.WriteLine("");
        _output.WriteLine($"=== LEAD docs (empty FG or EF_LEAD): {leadDocs.Count} ===");
        
        // Additional docs: ALL docs (no FormGroup filter in SubsequentFiling)
        _output.WriteLine($"=== ADDITIONAL docs (all): {subsequentFilingDocs.Count} ===");
        
        // Compare with competitor list
        var competitorDocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Abandonment: Appeal", "Abstract of Judgment Filed", "Abstract of Judgment Issued", "Advisement",
            "Affidavit of Summons: Publication", "Affidavit: 170.1 Disqualification", "Affidavit: 170.6 Disqualification",
            "Affidavit: Contempt", "Affidavit: Misc", "Agreement", "Amended Answer", "Amendment", "Answer",
            "Answer to Request for Statement of Witnesses and Evidence [CCP 96]", "Answer: Amended Complaint",
            "Answer: Cross Complaint", "Answer: Verified", "Application/ Order: Service by Posting or Publication",
            "Application/Declaration: TRO", "Application/Order: Extension of Time", "Application: Appear Pro Hac Vice",
            "Application: Other", "Application: Renew Judgment", "Application: Subpoena Discovery Out of State",
            "Application:OEX", "Argument", "Assessment", "Assignment", "Association or Co-Counsel", "Attachment",
            "Award: Arbitration", "Bond", "Brief", "Brief: Opening", "Brief: Respondent", "Case Management Conf Statement",
            "Certificate", "Certificate: Rehab/Restore CV Rts", "CH-115 Request to Continue Hearing",
            "CH-800 Receipt for Firearms and Firearm Parts", "Civil Case Cover Sheet", "Claim",
            "Claim of Exemption (Wage Garnishment) (WG-006)", "Claim to Right of Possession", "Complaint",
            "Complaint for Joinder", "Complaint: Amended", "Complaint: Amended-First", "Complaint: Cross",
            "Complaint: Cross-Amended", "Complaint: Small Claims Juris Limit", "Confidential CLETS Information",
            "Cover Sheet for Confidential Information (DV-175)", "Declaration",
            "DECLARATION IN SUPPORT OF ATTORNEY'S MOTION TO BE RELIEVED AS COUNSEL—CIVIL (MC-052)",
            "Declaration of Demurring or Moving Party in Support of Automatic Extension (CIV-141)",
            "Declaration of due diligence", "Declaration of Non-Service", "Declaration re: Reduced Filing Fees",
            "Declaration re: Venue", "Declaration regarding nonmilitary status", "Declaration: Ex Parte Notice",
            "Declaration: Lost Summons", "Declaration:Issue Warrant-Attchmnt", "Demand", "Demurrer", "Denial: General",
            "Designation", "Designation of Counsel", "Designation: Record on Appeal", "Disclaimer of Interest",
            "Dismissal: Appeal", "Dismissal: DCA", "Dismissal: Partial", "Document: Other", "EA-115 Request to Continue Hearing",
            "Ex Parte Application for Extension of Time to Serve Pleading (CM-020)",
            "Ex Parte Application for Extension of Time to Serve Pleading AND Order Continuing Case Management Conference (CM-020)",
            "Exhibit List", "Final Return to Court – Writ of Execution", "Findings/Order-After Hearing",
            "Gun Violence Emergency Protective Order (EPO-002)", "Gun Violence Restraining Order After Hearing on EPO-002 (GV-030)",
            "GV-115 Request to Continue Hearing", "GV-116 Order for Continuance and Notice of New Hearing Date",
            "Judgment: Amended", "Judgment: Assignment", "Judgment: Court", "Judgment: Default (Clerk)",
            "Judgment: Default (Court)", "Judgment: Other", "Judgment: Verdict", "Jury Instructions",
            "Lodging of Transcript Reproduction only per Government Code§ 69954(d)", "Log of Events",
            "Memo: Points and Authorities", "Memorandum", "Memorandum: At Issue", "Memorandum: At Issue-Counter",
            "Memorandum: Costs", "Memorandum: Decision", "Motion", "Motion: Change of Venue", "Motion: Quash",
            "Motion: Strike", "Motion: Summary Judgment", "Nomination", "Non-opposition", "Not Entry Dismiss & Pos",
            "Notice of Change of Handling Attorney", "Notice of Chapter 7 Bankruptcy Case RECEIVED (Form 309A for individual or joint debtors)",
            "Notice of Hearing on Claim of Exemption (Wage Garnishment – Enforcement of Judgment) (WG-010/EJ-175)",
            "NOTICE OF MOTION AND MOTION TO BE RELIEVED AS COUNSEL—CIVIL (MC-051)",
            "Notice Of Opposition to Claim of Exemption (WG-009)",
            "Notice of Petition and Petition Regarding Confiscation of Firearms [Welfare & Inst. Code § 8102 And 5150: Examination of Mental Condition — Firearms Seizure]",
            "Notice of Remote Appearance", "Notice of Unavailability of Counsel", "Notice to Appear",
            "Notice: Acknowledgement/Receipt", "Notice: Appeal - Adm Penalty / Fare Evade", "Notice: Appeal - Admin Fine or Penalty",
            "Notice: Appeal - Dangerous/Vicious Dog", "Notice: Appeal - Parking Violation", "Notice: Appeal - Unlimited",
            "Notice: Bankruptcy/Stay", "Notice: Change Address/Firm Name", "Notice: Conflict of Attorney",
            "Notice: Consolidation", "Notice: Default on Appeal", "Notice: Dismissal Appeal", "Notice: Entry of Dismissal/PofS",
            "Notice: Hearing", "Notice: Judgment Entry", "Notice: Judgment Renewal", "Notice: Lis Pendens", "Notice: Motion",
            "Notice: Opposition to Claim", "Notice: Other", "Notice: Posting Jury Fees", "Notice: Settlement", "Notice: Trial",
            "Oath", "Opposition/Objection", "Order", "Order Appointing Court-Approved Official Reporter Pro Tempore (MAD-RPT-002)",
            "ORDER APPROVING COMPROMISE OF CLAIM OR ACTION OR DISPOSITION OF PROCEEDS OF JUDGMENT FOR MINOR OR PERSON WITH A DISABILITY (MC-351)",
            "Order Determining Claim of Exemption (Wage Garnishment) (WG-011)",
            "Order for the Production of Reporter Pro Tempore Transcripts (MAD-RPT-001)", "Order for Writ of Issue",
            "ORDER GRANTING ATTORNEY'S MOTION TO BE RELIEVED AS COUNSEL—CIVIL (MC-053)", "Order: 170.1 Disqualification",
            "Order: Accounting and Report", "Order: Change of Venue", "Order: Deny Petition", "Order: Extension of Time",
            "Order: Fee Waiver - Set for Hearing", "Order: Fee Waiver (Appeal)", "Order: Fee Waiver (Trnscrpt)",
            "Order: Fee Waiver Payment", "Order: Fee Waiver Pending", "Order: Fee Waiver Pending – Subsequent",
            "Order: Fee Waiver-Deny", "Order: Fee Waiver-Deny-Subsequent", "Order: Fee Waiver-Grant",
            "Order: Fee Waiver-Grant-Subsequent", "Order: Fee Waiver-Partial", "Order: Fee Waiver-Partial-Subseqnt",
            "Order: Grant Petition", "Order: Media Coverage", "Order: Publication of Citation", "Order: Publication of Summons",
            "Order: Restraining-After Hearing", "Order: Setting Aside Default", "Order: Shortening Time", "Order: Show Cause",
            "OSC and TRO", "Petition", "Petition: Amended", "Petition: Appt Guardian Ad Litem", "Petition: Asset Forfeiture",
            "Petition: Confirm Award Arbitrator", "Petition: Injunction to Prohibit Workplace Harassment",
            "Petition: Minor's Compromise", "Petition: Name Change", "Petition: Non-Party Relief Discovery Out of State",
            "Petition: Protect Ord Elder Abuse", "Petition: Rehab/Restore Civil Rts", "Petition: Relief in Discovery Out of State",
            "Petition: Subsequent - Non Party Relief in Discovery Out of State", "Petition: Subsequent - Relief in Discovery Out of State",
            "Petition: Workplace Harassment", "Petition: Writ of Habeas Corpus",
            "Plaintiff's Mandatory Cover Sheet and Supplemental Allegations - Unlawful Detainer", "Preliminary Injunction",
            "Pretrial Order RE: Civil Jury Trials", "Procedural Stipulations - Civil", "Proof of Electronic Service",
            "Proof of Service of Summons", "Proof of Unsuccessful Service", "Proof/Certificate of Completion",
            "Proof: Publication", "Proof: Service", "Proof: Service Mail", "Proposed Order (Cover Sheet) (EFS-020)",
            "RECEIPT", "REDACTED - Request for Hearing for Relief from Firearms Prohibition (BOF 4009C)",
            "REDACTED - Request for Hearing to Challenge Disqualified Person Determination (BOF 1031)",
            "Reissuance of OSC", "Release of Lien or Claim", "Renewal: Judgment", "Reply", "Reply to Opposition",
            "Reply: Appellant", "Report", "Request", "Request for Calendar Setting - Civil Division (MAD-CIV-002)",
            "Request for Elder of Dependent Adult Abuse Restraining Orders (EA-100)", "Request for Interpreter Services - English (MAD-INT-001)",
            "Request for Refund", "REQUEST THAT CLERK ENTER JUDGMENT AND JUDGMENT ON FINAL CITATION AND NOTIFICATION OF PENALTY OF THE DIVISION OF OCCUPATIONAL SAFETY AND HEALTH",
            "Request to Set", "Request to Waive Addtl Court Fees", "Request to Waive Court Fees",
            "Request to Waive Court Fees (Appeal)", "Request to Waive Court Fees (Trnscrpt)", "Request/Order: Accommodations",
            "Request: Appeal Extension", "Request: Augment Record", "Request: Clerk's Judgment", "Request: Court's Judgment",
            "Request: Default", "Request: Default/Clerks' Judgment", "Request: Default/Court Judgment", "Request: Dismissal",
            "Request: Dismissal-Denied", "Request: Dismissal-Granted", "Request: Dismissal-Granted Full",
            "Request: Dismissal-Granted Partial", "Request: Entry of Judgment", "Request: Extension of Time",
            "Request: Judicial Notice", "Request: Media Coverage", "Request: Set Case for Trial - UD",
            "Request: Statement of Decision", "Request: Telephone Appearance", "Request: Trial De Novo", "Resignation",
            "Response", "Response to Request for Civil Harassment Restraining Orders (CH-120)", "Response: OSC or Motion",
            "Return on Attachment/Execution", "Satisfaction of Judgment Filed", "Satisfaction: Judgment-Full",
            "Satisfaction: Judgment-Partial", "Settlement Conference Statement", "Special Interrogatories", "Statement",
            "Statement of Venue", "Statement: Damages", "Statement: Other", "Statement: Undisputed Facts", "Stipulation",
            "Stipulation and Order", "Stipulation and Order to Continue Trial", "Stipulation: Commissioner",
            "Stipulation: Judge Pro Tem", "Subpoena: Filed", "Subpoena: Issued", "Substitution: Attorney", "Summary of Facts",
            "Summons: Filed", "Summons: Issued", "Transcription of Audio", "Trial Brief",
            "UD-150 Request/Counter-Request to Set Case for Trial—Unlawful Detainer",
            "Unlawful Detainer Supplemental Cover Sheet (MAD-CIV-018)",
            "UNREDACTED - Request for Hearing for Relief from Firearms Prohibition (BOF 4009C)",
            "UNREDACTED - Request for Hearing to Challenge Disqualified Person Determination (BOF 1031)",
            "Verification", "Verification by Landlord Regarding Rental Assistance - Unlawful Detainer",
            "Video Transcription", "Voir Dire Questions", "Waiver", "Waiver of Interest", "Warrant to Inspect and Abate",
            "Withdrawal", "Witness List", "Writ of Attachment (Filed)", "Writ of Attachment (Issued)",
            "Writ of Execution (Filed)", "Writ of Execution (Issued)", "Writ of Possession (Filed)",
            "Writ of Possession (Issued)", "Writ of Sale: Filed (EJ-130)", "Writ of Sale: Issued (EJ-130)",
            "Writ: Other", "WV-115 Request to Continue Hearing"
        };
        
        // Normalize names for comparison (trim, normalize whitespace)
        string Normalize(string s) => string.Join(" ", (s ?? "").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        
        var ourDocNames = subsequentFilingDocs.Select(d => Normalize(d.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var competitorNormalized = competitorDocs.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        // Find what competitor has that we don't
        var missingFromUs = competitorNormalized.Where(c => !ourDocNames.Contains(c)).OrderBy(x => x).ToList();
        
        // Find what we have that competitor doesn't
        var extraInOurs = ourDocNames.Where(o => !competitorNormalized.Contains(o)).OrderBy(x => x).ToList();
        
        _output.WriteLine("");
        _output.WriteLine("=============================================");
        _output.WriteLine("=== COMPARISON WITH COMPETITOR ===");
        _output.WriteLine("=============================================");
        _output.WriteLine($"Competitor count: {competitorDocs.Count}");
        _output.WriteLine($"Our count: {subsequentFilingDocs.Count}");
        _output.WriteLine("");
        _output.WriteLine($"=== MISSING FROM US ({missingFromUs.Count}): ===");
        foreach (var name in missingFromUs)
        {
            _output.WriteLine($"  - {name}");
        }
        _output.WriteLine("");
        _output.WriteLine($"=== EXTRA IN OURS ({extraInOurs.Count}): ===");
        foreach (var name in extraInOurs)
        {
            _output.WriteLine($"  + {name}");
        }
        
        // Check if missing docs exist in API but got filtered out
        _output.WriteLine("");
        _output.WriteLine("=== MISSING DOCS - SEARCHING FULL API ===");
        var foundInApi = 0;
        var notInApi = 0;
        var foundWithSubType = 0;
        foreach (var missingName in missingFromUs)
        {
            // Try exact match first (normalized)
            var found = docs.FirstOrDefault(d => Normalize(d.Name).Equals(missingName, StringComparison.OrdinalIgnoreCase));
            
            if (found != null)
            {
                foundInApi++;
                var matchesCaseType = allCodes.Any(tc => found.CaseTypes.Contains(tc) || found.CaseCategories.Contains(tc));
                var hasSubTypes = found.CaseSubTypes.Count > 0;
                var isUnrestricted = found.CaseTypes.Count == 0 && found.CaseCategories.Count == 0 && found.CaseSubTypes.Count == 0;
                
                if (hasSubTypes) foundWithSubType++;
                
                var reason = matchesCaseType ? "MATCHES" : (isUnrestricted ? "UNRESTRICTED" : (hasSubTypes ? "HAS_SUBTYPES" : "WRONG_CASETYPE"));
                _output.WriteLine($"  {reason}: '{missingName}'");
                _output.WriteLine($"    Code: {found.Code}");
                _output.WriteLine($"    CaseTypes: [{string.Join(",", found.CaseTypes.Take(5))}{(found.CaseTypes.Count > 5 ? "..." : "")}]");
                _output.WriteLine($"    CaseCategories: [{string.Join(",", found.CaseCategories.Take(5))}{(found.CaseCategories.Count > 5 ? "..." : "")}]");
                _output.WriteLine($"    CaseSubTypes: [{string.Join(",", found.CaseSubTypes.Take(5))}{(found.CaseSubTypes.Count > 5 ? "..." : "")}]");
                _output.WriteLine($"    FormGroups: [{string.Join(",", found.FormGroups)}]");
            }
            else
            {
                notInApi++;
                _output.WriteLine($"  NOT_IN_API: {missingName}");
            }
        }
        _output.WriteLine("");
        _output.WriteLine($"=== SUMMARY: Found: {foundInApi}, Not found: {notInApi}, Has SubTypes: {foundWithSubType} ===");
        
        // Check if ANY docs in API have CaseSubTypes set
        var docsWithSubTypes = docs.Where(d => d.CaseSubTypes.Count > 0).ToList();
        _output.WriteLine($"=== Total docs with CaseSubTypes: {docsWithSubTypes.Count} ===");
        if (docsWithSubTypes.Count > 0)
        {
            _output.WriteLine("Sample CaseSubType values:");
            var sampleSubTypes = docsWithSubTypes.SelectMany(d => d.CaseSubTypes).Distinct().Take(20).ToList();
            foreach (var st in sampleSubTypes) _output.WriteLine($"  - {st}");
        }
        
        // If docs exist but have wrong case type, competitor might not be filtering by case type at all
        if (foundInApi > 0)
        {
            _output.WriteLine("");
            _output.WriteLine("=== HYPOTHESIS: Competitor may show ALL docs without case type filtering ===");
            _output.WriteLine($"If we showed all {docs.Count} docs with names, we'd have many more than competitor's {competitorDocs.Count}");
            _output.WriteLine("Competitor likely uses a DIFFERENT filtering strategy.");
        }
    }
    
    /// <summary>
    /// Check if Required field is present/parsed correctly in document list.
    /// Look for patterns - maybe some docs have required=true and we're missing it.
    /// </summary>
    [Fact]
    public async Task AnalyzeRequiredFieldPatterns()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        
        var docs = await provider.GetDocumentListAsync(config, caseType: "411110", subFiling: true);
        
        // Find all unique metadata codes and their Required values
        var metaStats = docs
            .SelectMany(d => d.MetadataItems)
            .GroupBy(m => m.Code)
            .Select(g => new {
                Code = g.Key,
                TotalCount = g.Count(),
                RequiredTrue = g.Count(m => m.Required),
                RequiredFalse = g.Count(m => !m.Required)
            })
            .OrderByDescending(x => x.TotalCount)
            .ToList();
        
        _output.WriteLine($"=== Metadata Code Statistics (across {docs.Count} docs) ===\n");
        _output.WriteLine($"{"Code",-30} {"Total",-8} {"Req=true",-10} {"Req=false",-10}");
        _output.WriteLine(new string('-', 60));
        
        foreach (var stat in metaStats)
        {
            _output.WriteLine($"{stat.Code,-30} {stat.TotalCount,-8} {stat.RequiredTrue,-10} {stat.RequiredFalse,-10}");
        }
        
        // Key question: Is FILING_PARTY ever Required=true?
        var filingPartyRequired = docs
            .SelectMany(d => d.MetadataItems)
            .Where(m => m.Code == "FILING_PARTY" && m.Required)
            .ToList();
        
        _output.WriteLine($"\n=== FILING_PARTY with Required=true: {filingPartyRequired.Count} ===");
        
        // What about FILED_BY?
        var filedByRequired = docs
            .SelectMany(d => d.MetadataItems)
            .Where(m => m.Code == "FILED_BY" && m.Required)
            .ToList();
        
        _output.WriteLine($"=== FILED_BY with Required=true: {filedByRequired.Count} ===");
        
        // Show all unique metadata codes
        var allCodes = metaStats.Select(s => s.Code).ToList();
        _output.WriteLine($"\n=== All unique metadata codes ({allCodes.Count}): ===");
        foreach (var code in allCodes.OrderBy(c => c))
        {
            _output.WriteLine($"  - {code}");
        }
    }
    
    /// <summary>
    /// Dump RAW document metadata to see exactly what court returns for required field.
    /// </summary>
    [Fact]
    public async Task DumpRawDocumentMetadataXml()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        
        // Get all documents for subsequent filing
        var docs = await provider.GetDocumentListAsync(config, caseType: "411110", subFiling: true);
        
        // Find documents with interesting metadata patterns
        var docsWithMeta = docs.Where(d => d.MetadataItems.Count > 0).Take(20).ToList();
        
        _output.WriteLine($"=== Documents with Metadata ({docsWithMeta.Count}) ===\n");
        
        foreach (var doc in docsWithMeta)
        {
            _output.WriteLine($"📄 {doc.Name} (Code: {doc.Code})");
            _output.WriteLine($"   Metadata Items: {doc.MetadataItems.Count}");
            
            foreach (var meta in doc.MetadataItems)
            {
                var reqStatus = meta.Required ? "⚠️ REQUIRED" : "✅ optional";
                _output.WriteLine($"   - {meta.Code}: {reqStatus}");
                _output.WriteLine($"       ClassType: {meta.ClassType}, ValueRestriction: {meta.ValueRestriction ?? "(none)"}");
            }
            _output.WriteLine("");
        }
        
        // Check: Are there ANY documents where ANY metadata item is Required=true?
        var docsWithRequired = docs.Where(d => d.MetadataItems.Any(m => m.Required)).ToList();
        _output.WriteLine($"\n=== Documents with at least one REQUIRED metadata: {docsWithRequired.Count} ===");
        foreach (var doc in docsWithRequired.Take(10))
        {
            var reqItems = doc.MetadataItems.Where(m => m.Required).ToList();
            _output.WriteLine($"  {doc.Name}: {reqItems.Count} required items");
            foreach (var m in reqItems)
            {
                _output.WriteLine($"    - {m.Code} ({m.ClassType})");
            }
        }
    }
    
    /// <summary>
    /// Test to verify metadata for "Writ of Execution (Issued)" document type.
    /// Particularly interested in new party metadata (ValueRestriction="new-data").
    /// Case: Civil Unlimited (411110), Auto Tort 22 (401100)
    /// </summary>
    [Fact]
    public async Task VerifyWritOfExecutionMetadata()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        
        // Get all documents for subsequent filing
        var docs = await provider.GetDocumentListAsync(config, caseType: "411110", subFiling: true);
        _output.WriteLine($"Total docs for Civil Unlimited (411110): {docs.Count}");
        
        // Find "Writ of Execution (Issued)"
        var writOfExecution = docs.FirstOrDefault(d => 
            d.Name?.Contains("Writ of Execution", StringComparison.OrdinalIgnoreCase) == true &&
            d.Name?.Contains("Issued", StringComparison.OrdinalIgnoreCase) == true);
        
        if (writOfExecution == null)
        {
            // Try without caseType filter
            var allDocs = await provider.GetDocumentListAsync(config, caseType: null, subFiling: true);
            writOfExecution = allDocs.FirstOrDefault(d => 
                d.Name?.Contains("Writ of Execution", StringComparison.OrdinalIgnoreCase) == true &&
                d.Name?.Contains("Issued", StringComparison.OrdinalIgnoreCase) == true);
            _output.WriteLine($"Searched in all {allDocs.Count} docs");
        }
        
        Assert.NotNull(writOfExecution);
        _output.WriteLine($"\n=== Document Found ===");
        _output.WriteLine($"Code: {writOfExecution.Code}");
        _output.WriteLine($"Name: {writOfExecution.Name}");
        _output.WriteLine($"FormGroups: [{string.Join(", ", writOfExecution.FormGroups)}]");
        _output.WriteLine($"CaseTypes: [{string.Join(", ", writOfExecution.CaseTypes.Take(5))}{(writOfExecution.CaseTypes.Count > 5 ? "..." : "")}]");
        _output.WriteLine($"EfmRequiresSubCase: {writOfExecution.EfmRequiresSubCase}");
        
        _output.WriteLine($"\n=== Metadata Items ({writOfExecution.MetadataItems.Count}) ===");
        foreach (var meta in writOfExecution.MetadataItems)
        {
            _output.WriteLine($"\n  [{meta.Code}]");
            _output.WriteLine($"    Name: {meta.Name}");
            _output.WriteLine($"    ClassType: {meta.ClassType}");
            _output.WriteLine($"    Required: {meta.Required}");
            _output.WriteLine($"    Multiple: {meta.Multiple}");
            _output.WriteLine($"    ValueRestriction: {meta.ValueRestriction ?? "(none)"}");
            _output.WriteLine($"    Filter: {meta.Filter ?? "(none)"}");
            if (meta.AdditionalInfoTags.Count > 0)
            {
                _output.WriteLine($"    AdditionalInfoTags: [{string.Join(", ", meta.AdditionalInfoTags)}]");
            }
            if (meta.PartyTypes.Count > 0)
            {
                _output.WriteLine($"    PartyTypes: [{string.Join(", ", meta.PartyTypes)}]");
            }
        }
        
        // Specifically check for new-data metadata items
        var newDataItems = writOfExecution.MetadataItems
            .Where(m => m.ValueRestriction?.Equals("new-data", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        
        _output.WriteLine($"\n=== NEW-DATA Metadata Items ({newDataItems.Count}) ===");
        foreach (var meta in newDataItems)
        {
            _output.WriteLine($"  {meta.Code}: ClassType={meta.ClassType}, Required={meta.Required}");
            if (meta.PartyTypes.Count > 0)
            {
                _output.WriteLine($"    PartyTypes available: [{string.Join(", ", meta.PartyTypes)}]");
            }
        }
        
        // Check for caseParticipant metadata items
        var partyItems = writOfExecution.MetadataItems
            .Where(m => m.ClassType?.Equals("caseParticipant", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        
        _output.WriteLine($"\n=== caseParticipant Metadata Items ({partyItems.Count}) ===");
        foreach (var meta in partyItems)
        {
            _output.WriteLine($"  {meta.Code}: Name={meta.Name}, Required={meta.Required}, ValueRestriction={meta.ValueRestriction ?? "(none)"}");
        }
        
        // Check for attorney/caseAssignment metadata items
        var attorneyItems = writOfExecution.MetadataItems
            .Where(m => m.ClassType?.Equals("caseAssignment", StringComparison.OrdinalIgnoreCase) == true ||
                       m.ClassType?.Equals("attorney", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        
        _output.WriteLine($"\n=== Attorney/caseAssignment Metadata Items ({attorneyItems.Count}) ===");
        foreach (var meta in attorneyItems)
        {
            _output.WriteLine($"  {meta.Code}: Name={meta.Name}, Required={meta.Required}, ValueRestriction={meta.ValueRestriction ?? "(none)"}");
        }
    }
}

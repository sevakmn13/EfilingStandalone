using EFiling.Providers.JTI.Parsers;

namespace EFiling.Tests;

/// <summary>
/// Unit tests for CodeListResponseParser — verifies parsing of Genericode 1.0
/// code list, court location, attorney, and document list XML responses.
/// </summary>
public class CodeListParserTests
{
    // ─── ParseCodeList ──────────────────────────────────────────────

    [Fact]
    public void ParseCodeList_StandardItems_ExtractsCodeAndName()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>CV</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:cv=""urn:com.journaltech:ecourt:ecf:extension:CodeValue"">
                        <cv:name>Civil</cv:name>
                    </ComplexValue>
                </Value>
            </Row>
            <Row>
                <Value ColumnRef=""code""><SimpleValue>FM</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:cv=""urn:com.journaltech:ecourt:ecf:extension:CodeValue"">
                        <cv:name>Family</cv:name>
                    </ComplexValue>
                </Value>
            </Row>");

        var items = CodeListResponseParser.ParseCodeList(xml);

        Assert.Equal(2, items.Count);
        Assert.Equal("CV", items[0].Code);
        Assert.Equal("Civil", items[0].Name);
        Assert.Equal("FM", items[1].Code);
        Assert.Equal("Family", items[1].Name);
    }

    [Fact]
    public void ParseCodeList_WithRelationships_ExtractsRelatedListAndCode()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>10101</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:cv=""urn:com.journaltech:ecourt:ecf:extension:CodeValue"">
                        <cv:name>Motor Vehicle - Personal Injury/Property Damage/Wrongful Death</cv:name>
                        <cv:relationships>
                            <cv:relationship>
                                <cv:relatedListName>CASE_TYPE</cv:relatedListName>
                                <cv:relatedCode>421110</cv:relatedCode>
                            </cv:relationship>
                        </cv:relationships>
                    </ComplexValue>
                </Value>
            </Row>");

        var items = CodeListResponseParser.ParseCodeList(xml);

        Assert.Single(items);
        Assert.Equal("10101", items[0].Code);
        Assert.Single(items[0].Relationships);
        Assert.Equal("CASE_TYPE", items[0].Relationships[0].RelatedListName);
        Assert.Equal("421110", items[0].Relationships[0].RelatedCode);
    }

    [Fact]
    public void ParseCodeList_MultipleRelationships()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>PLAIN</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:cv=""urn:com.journaltech:ecourt:ecf:extension:CodeValue"">
                        <cv:name>Plaintiff</cv:name>
                        <cv:relationships>
                            <cv:relationship>
                                <cv:relatedListName>CASE_TYPE</cv:relatedListName>
                                <cv:relatedCode>421110</cv:relatedCode>
                            </cv:relationship>
                            <cv:relationship>
                                <cv:relatedListName>CASE_TYPE</cv:relatedListName>
                                <cv:relatedCode>422110</cv:relatedCode>
                            </cv:relationship>
                        </cv:relationships>
                    </ComplexValue>
                </Value>
            </Row>");

        var items = CodeListResponseParser.ParseCodeList(xml);

        Assert.Single(items);
        Assert.Equal(2, items[0].Relationships.Count);
    }

    [Fact]
    public void ParseCodeList_NoRelationships_ReturnsEmptyList()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>US</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:cv=""urn:com.journaltech:ecourt:ecf:extension:CodeValue"">
                        <cv:name>United States</cv:name>
                    </ComplexValue>
                </Value>
            </Row>");

        var items = CodeListResponseParser.ParseCodeList(xml);

        Assert.Single(items);
        Assert.Empty(items[0].Relationships);
    }

    [Fact]
    public void ParseCodeList_EmptyRows_ReturnsEmpty()
    {
        var xml = BuildCodeListXml("");
        var items = CodeListResponseParser.ParseCodeList(xml);
        Assert.Empty(items);
    }

    [Fact]
    public void ParseCodeList_SkipsRowsWithEmptyCode()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue></SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:cv=""urn:com.journaltech:ecourt:ecf:extension:CodeValue"">
                        <cv:name>Empty Code</cv:name>
                    </ComplexValue>
                </Value>
            </Row>");

        var items = CodeListResponseParser.ParseCodeList(xml);
        Assert.Empty(items);
    }

    [Fact]
    public void ParseCodeList_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => CodeListResponseParser.ParseCodeList(""));
    }

    // ─── ParseCourtLocations ────────────────────────────────────────

    [Fact]
    public void ParseCourtLocations_ExtractsLocationCodeAndName()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:zv=""urn:com.journaltech:ecourt:ecf:extension:ZipCodeValue"">
                        <zv:zipCode>93637</zv:zipCode>
                        <zv:locationCode>MAD</zv:locationCode>
                        <zv:locationName>Madera Courthouse</zv:locationName>
                        <zv:caseType>CV</zv:caseType>
                    </ComplexValue>
                </Value>
            </Row>");

        var locations = CodeListResponseParser.ParseCourtLocations(xml);

        Assert.Single(locations);
        Assert.Equal("MAD", locations[0].LocationCode);
        Assert.Equal("Madera Courthouse", locations[0].LocationName);
    }

    [Fact]
    public void ParseCourtLocations_DeduplicatesByLocationCode()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:zv=""urn:com.journaltech:ecourt:ecf:extension:ZipCodeValue"">
                        <zv:locationCode>MAD</zv:locationCode>
                        <zv:locationName>Madera Courthouse</zv:locationName>
                        <zv:caseType>CV</zv:caseType>
                    </ComplexValue>
                </Value>
            </Row>
            <Row>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:zv=""urn:com.journaltech:ecourt:ecf:extension:ZipCodeValue"">
                        <zv:locationCode>MAD</zv:locationCode>
                        <zv:locationName>Madera Courthouse</zv:locationName>
                        <zv:caseType>FM</zv:caseType>
                    </ComplexValue>
                </Value>
            </Row>");

        var locations = CodeListResponseParser.ParseCourtLocations(xml);
        Assert.Single(locations);
    }

    [Fact]
    public void ParseCourtLocations_MultipleLocations()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:zv=""urn:com.journaltech:ecourt:ecf:extension:ZipCodeValue"">
                        <zv:locationCode>MAD</zv:locationCode>
                        <zv:locationName>Madera</zv:locationName>
                    </ComplexValue>
                </Value>
            </Row>
            <Row>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:zv=""urn:com.journaltech:ecourt:ecf:extension:ZipCodeValue"">
                        <zv:locationCode>CHW</zv:locationCode>
                        <zv:locationName>Chowchilla</zv:locationName>
                    </ComplexValue>
                </Value>
            </Row>");

        var locations = CodeListResponseParser.ParseCourtLocations(xml);
        Assert.Equal(2, locations.Count);
    }

    [Fact]
    public void ParseCourtLocations_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => CodeListResponseParser.ParseCourtLocations(""));
    }

    // ─── ParseAttorneyList ──────────────────────────────────────────

    [Fact]
    public void ParseAttorneyList_ExtractsAllFields()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:av=""urn:com.journaltech:ecourt:ecf:extension:AttorneyValue"">
                        <av:id>1001</av:id>
                        <av:barNumber>123456</av:barNumber>
                        <av:firstName>Jane</av:firstName>
                        <av:middleName>M</av:middleName>
                        <av:lastName>Doe</av:lastName>
                        <av:lastNameSuffix>Jr</av:lastNameSuffix>
                        <av:firmName>Doe &amp; Associates</av:firmName>
                        <av:address1>100 Main St</av:address1>
                        <av:address2>Suite 200</av:address2>
                        <av:city>Madera</av:city>
                        <av:state>CA</av:state>
                        <av:zip>93637</av:zip>
                        <av:country>US</av:country>
                        <av:phoneNumber>559-555-1234</av:phoneNumber>
                        <av:email>jane@doelaw.com</av:email>
                        <av:statusCode>A</av:statusCode>
                        <av:participationStatus>Active</av:participationStatus>
                    </ComplexValue>
                </Value>
            </Row>");

        var attorneys = CodeListResponseParser.ParseAttorneyList(xml);

        Assert.Single(attorneys);
        var a = attorneys[0];
        Assert.Equal("1001", a.Id);
        Assert.Equal("123456", a.BarNumber);
        Assert.Equal("Jane", a.FirstName);
        Assert.Equal("M", a.MiddleName);
        Assert.Equal("Doe", a.LastName);
        Assert.Equal("Jr", a.LastNameSuffix);
        Assert.Equal("Doe & Associates", a.FirmName);
        Assert.Equal("100 Main St", a.Address1);
        Assert.Equal("Suite 200", a.Address2);
        Assert.Equal("Madera", a.City);
        Assert.Equal("CA", a.State);
        Assert.Equal("93637", a.Zip);
        Assert.Equal("US", a.Country);
        Assert.Equal("559-555-1234", a.PhoneNumber);
        Assert.Equal("jane@doelaw.com", a.Email);
        Assert.Equal("A", a.StatusCode);
        Assert.Equal("Active", a.ParticipationStatus);
    }

    [Fact]
    public void ParseAttorneyList_MultipleAttorneys()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:av=""urn:com.journaltech:ecourt:ecf:extension:AttorneyValue"">
                        <av:barNumber>111111</av:barNumber>
                        <av:firstName>John</av:firstName>
                        <av:lastName>Smith</av:lastName>
                    </ComplexValue>
                </Value>
            </Row>
            <Row>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:av=""urn:com.journaltech:ecourt:ecf:extension:AttorneyValue"">
                        <av:barNumber>222222</av:barNumber>
                        <av:firstName>Jane</av:firstName>
                        <av:lastName>Doe</av:lastName>
                    </ComplexValue>
                </Value>
            </Row>");

        var attorneys = CodeListResponseParser.ParseAttorneyList(xml);
        Assert.Equal(2, attorneys.Count);
        Assert.Equal("111111", attorneys[0].BarNumber);
        Assert.Equal("222222", attorneys[1].BarNumber);
    }

    [Fact]
    public void ParseAttorneyList_EmptyFields_ReturnsNull()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:av=""urn:com.journaltech:ecourt:ecf:extension:AttorneyValue"">
                        <av:barNumber>123456</av:barNumber>
                        <av:firstName>Jane</av:firstName>
                        <av:lastName>Doe</av:lastName>
                        <av:middleName>  </av:middleName>
                        <av:firmName></av:firmName>
                    </ComplexValue>
                </Value>
            </Row>");

        var attorneys = CodeListResponseParser.ParseAttorneyList(xml);
        Assert.Single(attorneys);
        Assert.Null(attorneys[0].MiddleName);
        Assert.Null(attorneys[0].FirmName);
    }

    [Fact]
    public void ParseAttorneyList_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => CodeListResponseParser.ParseAttorneyList(""));
    }

    // ─── ParseDocumentList ──────────────────────────────────────────

    [Fact]
    public void ParseDocumentList_ExtractsCodeNameAndFormGroups()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>COM040</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:dv=""urn:com.journaltech:ecourt:ecf:extension:DocumentValue"">
                        <dv:name>Complaint</dv:name>
                        <dv:efmRequiresSubCase>false</dv:efmRequiresSubCase>
                        <dv:caseTypes><dv:caseType>421110</dv:caseType></dv:caseTypes>
                        <dv:caseCategories><dv:caseCategory>10101</dv:caseCategory><dv:caseCategory>10102</dv:caseCategory></dv:caseCategories>
                        <dv:formGroups><dv:formGroup>EFCI_LEAD</dv:formGroup></dv:formGroups>
                    </ComplexValue>
                </Value>
            </Row>");

        var docs = CodeListResponseParser.ParseDocumentList(xml);

        Assert.Single(docs);
        var d = docs[0];
        Assert.Equal("COM040", d.Code);
        Assert.Equal("Complaint", d.Name);
        Assert.False(d.EfmRequiresSubCase);
        Assert.Single(d.CaseTypes);
        Assert.Equal("421110", d.CaseTypes[0]);
        Assert.Equal(2, d.CaseCategories.Count);
        Assert.Single(d.FormGroups);
        Assert.Equal("EFCI_LEAD", d.FormGroups[0]);
    }

    [Fact]
    public void ParseDocumentList_WithMetadataItems()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>401011</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:dv=""urn:com.journaltech:ecourt:ecf:extension:DocumentValue"">
                        <dv:name>First Paper</dv:name>
                        <dv:metadata>
                            <dv:metadataItems>
                                <dv:code>FILED_BY</dv:code>
                                <dv:name>Filed By</dv:name>
                                <dv:required>true</dv:required>
                                <dv:multiple>false</dv:multiple>
                                <dv:classType>caseParticipant</dv:classType>
                                <dv:filter>caseParticipant-all-parties-except-attorney</dv:filter>
                                <dv:valueRestriction>existing-data</dv:valueRestriction>
                            </dv:metadataItems>
                            <dv:metadataItems>
                                <dv:code>AS_TO</dv:code>
                                <dv:name>As To</dv:name>
                                <dv:required>false</dv:required>
                                <dv:multiple>true</dv:multiple>
                                <dv:classType>caseParticipant</dv:classType>
                            </dv:metadataItems>
                        </dv:metadata>
                    </ComplexValue>
                </Value>
            </Row>");

        var docs = CodeListResponseParser.ParseDocumentList(xml);

        Assert.Single(docs);
        Assert.Equal(2, docs[0].MetadataItems.Count);

        var filedBy = docs[0].MetadataItems[0];
        Assert.Equal("FILED_BY", filedBy.Code);
        Assert.Equal("Filed By", filedBy.Name);
        Assert.True(filedBy.Required);
        Assert.False(filedBy.Multiple);
        Assert.Equal("caseParticipant", filedBy.ClassType);
        Assert.Equal("caseParticipant-all-parties-except-attorney", filedBy.Filter);
        Assert.Equal("existing-data", filedBy.ValueRestriction);

        var asTo = docs[0].MetadataItems[1];
        Assert.Equal("AS_TO", asTo.Code);
        Assert.False(asTo.Required);
        Assert.True(asTo.Multiple);
    }

    [Fact]
    public void ParseDocumentList_WithAdditionalInfoTags()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>401574</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:dv=""urn:com.journaltech:ecourt:ecf:extension:DocumentValue"">
                        <dv:name>First Paper (Gov)</dv:name>
                        <dv:metadata>
                            <dv:metadataItems>
                                <dv:code>FILING_PARTY</dv:code>
                                <dv:name>Filing Party</dv:name>
                                <dv:classType>caseParticipant</dv:classType>
                                <dv:additionalInfoTags>FEE_EXEMPTION,GOVT_ENTITY</dv:additionalInfoTags>
                            </dv:metadataItems>
                        </dv:metadata>
                    </ComplexValue>
                </Value>
            </Row>");

        var docs = CodeListResponseParser.ParseDocumentList(xml);
        var meta = docs[0].MetadataItems[0];

        Assert.Equal(2, meta.AdditionalInfoTags.Count);
        Assert.Contains("FEE_EXEMPTION", meta.AdditionalInfoTags);
        Assert.Contains("GOVT_ENTITY", meta.AdditionalInfoTags);
    }

    [Fact]
    public void ParseDocumentList_CodeListMetadata_PopulatesCodeListSourceDistinctFromCode()
    {
        // Regression (F-F3): the <codeList> source name (e.g. CL_MOTION_OSC_DETAIL) is distinct
        // from the item Code (e.g. MOTION_OSC_DETAIL). The parser previously dropped <codeList>,
        // so the UI dropdown fell back to Code and queried the wrong codelist type → empty result.
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>244120</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:dv=""urn:com.journaltech:ecourt:ecf:extension:DocumentValue"">
                        <dv:name>DV-110 Temporary Restraining Order</dv:name>
                        <dv:metadata>
                            <dv:metadataItems>
                                <dv:code>MOTION_OSC_DETAIL</dv:code>
                                <dv:name>Motion/OSC Detail</dv:name>
                                <dv:required>true</dv:required>
                                <dv:classType>codeList</dv:classType>
                                <dv:valueRestriction>new-data</dv:valueRestriction>
                                <dv:codeList>CL_MOTION_OSC_DETAIL</dv:codeList>
                            </dv:metadataItems>
                        </dv:metadata>
                    </ComplexValue>
                </Value>
            </Row>");

        var docs = CodeListResponseParser.ParseDocumentList(xml);
        var meta = docs[0].MetadataItems[0];

        Assert.Equal("codeList", meta.ClassType);
        Assert.Equal("MOTION_OSC_DETAIL", meta.Code);
        Assert.Equal("CL_MOTION_OSC_DETAIL", meta.CodeList);
    }

    [Fact]
    public void ParseDocumentList_EfmRequiresSubCase_True()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>401565</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:dv=""urn:com.journaltech:ecourt:ecf:extension:DocumentValue"">
                        <dv:name>Filing on subcase</dv:name>
                        <dv:efmRequiresSubCase>true</dv:efmRequiresSubCase>
                    </ComplexValue>
                </Value>
            </Row>");

        var docs = CodeListResponseParser.ParseDocumentList(xml);
        Assert.True(docs[0].EfmRequiresSubCase);
    }

    [Fact]
    public void ParseDocumentList_WithCaseSubTypes()
    {
        var xml = BuildCodeListXml(@"
            <Row>
                <Value ColumnRef=""code""><SimpleValue>500100</SimpleValue></Value>
                <Value ColumnRef=""value"">
                    <ComplexValue xmlns:dv=""urn:com.journaltech:ecourt:ecf:extension:DocumentValue"">
                        <dv:name>Appeal Doc</dv:name>
                        <dv:caseSubTypes><dv:caseSubType>LIM</dv:caseSubType><dv:caseSubType>UNL</dv:caseSubType></dv:caseSubTypes>
                    </ComplexValue>
                </Value>
            </Row>");

        var docs = CodeListResponseParser.ParseDocumentList(xml);
        Assert.Equal(2, docs[0].CaseSubTypes.Count);
        Assert.Equal("LIM", docs[0].CaseSubTypes[0]);
        Assert.Equal("UNL", docs[0].CaseSubTypes[1]);
    }

    [Fact]
    public void ParseDocumentList_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => CodeListResponseParser.ParseDocumentList(""));
    }

    // ─── Helper ─────────────────────────────────────────────────────

    private static string BuildCodeListXml(string rows)
    {
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<ns2:CodeList xmlns:ns2=""http://docs.oasis-open.org/codelist/ns/genericode/1.0/"">
  <SimpleCodeList>
    {rows}
  </SimpleCodeList>
</ns2:CodeList>";
    }
}

namespace S1kdTools.Tests;

/// <summary>Shared XML fixtures for tests.</summary>
public static class Fixtures
{
    /// <summary>A minimal but representative Issue 4.x+ data module.</summary>
    public const string DataModule =
        """
        <dmodule>
          <identAndStatusSection>
            <dmAddress>
              <dmIdent>
                <dmCode modelIdentCode="EX" systemDiffCode="A" systemCode="00"
                        subSystemCode="0" subSubSystemCode="0" assyCode="00"
                        disassyCode="00" disassyCodeVariant="A" infoCode="040"
                        infoCodeVariant="A" itemLocationCode="D"/>
                <language languageIsoCode="en" countryIsoCode="CA"/>
                <issueInfo issueNumber="002" inWork="01"/>
              </dmIdent>
              <dmAddressItems>
                <issueDate year="2026" month="06" day="25"/>
                <dmTitle>
                  <techName>Example</techName>
                  <infoName>Description</infoName>
                </dmTitle>
              </dmAddressItems>
            </dmAddress>
            <dmStatus issueType="changed">
              <security securityClassification="01"/>
              <responsiblePartnerCompany enterpriseCode="ABCDE">
                <enterpriseName>Example Company</enterpriseName>
              </responsiblePartnerCompany>
              <originator enterpriseCode="ABCDE">
                <enterpriseName>Example Company</enterpriseName>
              </originator>
            </dmStatus>
          </identAndStatusSection>
          <content>
            <description>
              <para>Hello.</para>
            </description>
          </content>
        </dmodule>
        """;
}

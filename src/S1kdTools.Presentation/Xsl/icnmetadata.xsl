<?xml version="1.0" encoding="UTF-8"?>
<!--
  icnmetadata.xsl — ICN metadata file (icnmetadata.xsd).

  An ICN metadata file describes illustrations that are not themselves XML: the
  identifier, title, security and provenance of each information control number.
  It prints as a data sheet — the record an illustration library keeps.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template name="document-body">
    <xsl:call-template name="icn-record">
      <xsl:with-param name="ident" select="/icnMetadataFile/imfIdentAndStatusSection/imfAddress"/>
    </xsl:call-template>
    <xsl:apply-templates select="/icnMetadataFile/*[not(self::imfIdentAndStatusSection)]"/>
  </xsl:template>

  <xsl:template name="icn-record">
    <xsl:param name="ident"/>
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Illustration record'"/>
    </xsl:call-template>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" space-after="4mm">
      <fo:table-column column-width="{$body-w * 0.3}mm"/>
      <fo:table-column column-width="{$body-w * 0.7}mm"/>
      <fo:table-body>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'ICN'"/>
          <xsl:with-param name="value" select="$ident/imfIdent/imfCode/@imfIdentIcn"/>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Title'"/>
          <xsl:with-param name="value" select="$ident/imfAddressItems/icnTitle"/>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Issue'"/>
          <xsl:with-param name="value">
            <xsl:value-of select="$ident/imfIdent/issueInfo/@issueNumber"/>
            <xsl:text>-</xsl:text>
            <xsl:value-of select="$ident/imfIdent/issueInfo/@inWork"/>
          </xsl:with-param>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Issue date'"/>
          <xsl:with-param name="value">
            <xsl:call-template name="format-date">
              <xsl:with-param name="date" select="$ident/imfAddressItems/issueDate"/>
            </xsl:call-template>
          </xsl:with-param>
        </xsl:call-template>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <!-- One block per illustration the file describes. -->
  <xsl:template match="icn">
    <fo:block space-before="3mm" keep-together.within-page="always">
      <fo:block font-weight="bold" background-color="{$shade}" border="{$cell-rule}"
                padding="1.2mm" space-after="1.5mm">
        <xsl:value-of select="@infoEntityIdent|@icnIdent"/>
      </fo:block>
      <fo:block start-indent="4mm">
        <xsl:call-template name="attribute-table"/>
        <xsl:apply-templates/>
      </fo:block>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

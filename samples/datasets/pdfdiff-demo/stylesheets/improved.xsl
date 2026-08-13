<?xml version="1.0" encoding="UTF-8"?>
<!--
  improved.xsl — one round of reverse engineering later.

  This is baseline.xsl after acting on the first report's "What to change next" list, and
  nothing more: the page geometry, the body size and leading, the running header and
  footer, the section numbering and the page break before each section. Everything the
  report ranked below those — the warning box, the table rules and shading, the list
  labels, the ruled title block — is still missing on purpose.

  It exists so the demonstration can show the parity score doing the one job it is for:
  moving, for reasons you can name, when the stylesheet gets closer.
-->
<xsl:stylesheet version="1.0"
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:output method="xml" indent="no" encoding="UTF-8"/>

  <xsl:param name="publication" select="'AIRCRAFT MAINTENANCE MANUAL'"/>

  <xsl:template match="/dmodule">
    <fo:root font-family="serif">
      <fo:layout-master-set>
        <fo:simple-page-master master-name="body"
          page-width="210mm" page-height="297mm"
          margin-left="25mm" margin-right="18mm"
          margin-top="12mm" margin-bottom="12mm">
          <fo:region-body margin-top="14mm" margin-bottom="13mm"/>
          <fo:region-before extent="12mm"/>
          <fo:region-after extent="11mm"/>
        </fo:simple-page-master>
      </fo:layout-master-set>
      <fo:page-sequence master-reference="body">
        <fo:static-content flow-name="xsl-region-before">
          <fo:block font-size="8pt" border-after-style="solid"
            border-after-width="0.7pt" padding-after="3pt">
            <xsl:value-of select="$publication"/>
            <fo:leader leader-pattern="space"/>
            <xsl:apply-templates select="identAndStatusSection/dmAddress/dmIdent/dmCode" mode="code"/>
          </fo:block>
        </fo:static-content>
        <fo:static-content flow-name="xsl-region-after">
          <fo:block font-size="8pt" border-before-style="solid"
            border-before-width="0.4pt" padding-before="3pt">
            <xsl:call-template name="issue-date"/>
            <fo:leader leader-pattern="space"/>
            <fo:inline>Page </fo:inline>
            <fo:page-number/>
          </fo:block>
        </fo:static-content>
        <fo:flow flow-name="xsl-region-body">
          <xsl:apply-templates select="identAndStatusSection/dmAddress/dmAddressItems/dmTitle"/>
          <xsl:apply-templates select="content/description"/>
        </fo:flow>
      </fo:page-sequence>
    </fo:root>
  </xsl:template>

  <xsl:template match="dmCode" mode="code">
    <xsl:text>DMC-</xsl:text>
    <xsl:value-of select="@modelIdentCode"/>-<xsl:value-of select="@systemDiffCode"/>
    <xsl:text>-</xsl:text>
    <xsl:value-of select="@systemCode"/>-<xsl:value-of select="@subSystemCode"/>
    <xsl:value-of select="@subSubSystemCode"/>-<xsl:value-of select="@assyCode"/>
    <xsl:text>-</xsl:text>
    <xsl:value-of select="@disassyCode"/><xsl:value-of select="@disassyCodeVariant"/>
    <xsl:text>-</xsl:text>
    <xsl:value-of select="@infoCode"/><xsl:value-of select="@infoCodeVariant"/>
    <xsl:value-of select="@itemLocationCode"/>
  </xsl:template>

  <xsl:template name="issue-date">
    <xsl:variable name="d" select="/dmodule/identAndStatusSection/dmAddress/dmAddressItems/issueDate"/>
    <xsl:text>Issue </xsl:text>
    <xsl:value-of select="/dmodule/identAndStatusSection/dmAddress/dmIdent/issueInfo/@issueNumber"/>
    <xsl:text> — </xsl:text>
    <xsl:value-of select="$d/@year"/>-<xsl:value-of select="$d/@month"/>-<xsl:value-of select="$d/@day"/>
  </xsl:template>

  <xsl:template match="dmTitle">
    <fo:block font-size="18pt" font-weight="bold" space-after="8pt">
      <xsl:value-of select="techName"/>
    </fo:block>
    <fo:block font-size="12pt" font-style="italic" space-after="14pt">
      <xsl:value-of select="infoName"/>
    </fo:block>
  </xsl:template>

  <xsl:template match="description/levelledPara">
    <fo:block>
      <xsl:if test="preceding-sibling::levelledPara">
        <xsl:attribute name="break-before">page</xsl:attribute>
      </xsl:if>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="levelledPara">
    <fo:block space-before="10pt"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template match="levelledPara/title">
    <fo:block space-after="6pt" font-weight="bold">
      <xsl:attribute name="font-size">
        <xsl:choose>
          <xsl:when test="count(ancestor::levelledPara) = 1">14pt</xsl:when>
          <xsl:when test="count(ancestor::levelledPara) = 2">11.5pt</xsl:when>
          <xsl:otherwise>10pt</xsl:otherwise>
        </xsl:choose>
      </xsl:attribute>
      <xsl:for-each select="ancestor::levelledPara">
        <xsl:number count="levelledPara" level="single"/>
        <xsl:text>.</xsl:text>
      </xsl:for-each>
      <xsl:text>  </xsl:text>
      <xsl:value-of select="."/>
    </fo:block>
  </xsl:template>

  <xsl:template match="para">
    <fo:block font-size="10pt" line-height="12.5pt" text-align="justify"
      space-after="6pt" start-indent="6mm">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="emphasis">
    <fo:inline font-weight="bold"><xsl:apply-templates/></fo:inline>
  </xsl:template>

  <!-- Still unstyled: no box, no shading, no rules. The next round's work. -->
  <xsl:template match="warning | caution | note">
    <fo:block space-before="6pt" space-after="6pt" start-indent="6mm">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="warningAndCautionPara | notePara">
    <fo:block font-size="9pt" line-height="11pt"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template match="randomList">
    <fo:block space-after="6pt" start-indent="6mm"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template match="listItem">
    <fo:block><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template match="listItem/para">
    <fo:block font-size="10pt" line-height="12.5pt"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template match="table">
    <fo:block space-before="8pt" space-after="10pt" start-indent="6mm">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="table/title">
    <fo:block font-size="9pt" font-weight="bold" space-after="4pt">
      <xsl:text>Table </xsl:text>
      <xsl:number count="table" level="any"/>
      <xsl:text>  </xsl:text>
      <xsl:value-of select="."/>
    </fo:block>
  </xsl:template>

  <xsl:template match="colspec"/>

  <xsl:template match="row">
    <fo:block font-size="9pt"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template match="entry">
    <fo:inline><xsl:apply-templates/><xsl:text>  </xsl:text></fo:inline>
  </xsl:template>

  <xsl:template match="entry/para">
    <xsl:apply-templates/>
  </xsl:template>

</xsl:stylesheet>

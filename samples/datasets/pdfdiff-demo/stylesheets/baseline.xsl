<?xml version="1.0" encoding="UTF-8"?>
<!--
  baseline.xsl — the placeholder you start from.

  Everything the data module says, in one column, at one size, with default margins and
  no page furniture. It is not wrong: it is the honest first draft you write before you
  know what the target looks like, and it is what s1kd-pdfdiff is meant to be pointed at
  on day one.

  Compared against reference.xsl it is missing, on purpose: the running header and
  footer, the ruled title block, the section numbering, the page break before each
  section, the shaded warning box, the table rules and shading, the list labels, the
  justified measure, the leading, and the margins. Each of those is something the
  comparison should be able to name.
-->
<xsl:stylesheet version="1.0"
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:output method="xml" indent="no" encoding="UTF-8"/>

  <xsl:template match="/dmodule">
    <fo:root font-family="serif">
      <fo:layout-master-set>
        <fo:simple-page-master master-name="body"
          page-width="210mm" page-height="297mm" margin="20mm">
          <fo:region-body/>
        </fo:simple-page-master>
      </fo:layout-master-set>
      <fo:page-sequence master-reference="body">
        <fo:flow flow-name="xsl-region-body">
          <fo:block font-size="12pt">
            <xsl:apply-templates select="identAndStatusSection/dmAddress/dmAddressItems/dmTitle"/>
            <xsl:apply-templates select="content/description"/>
          </fo:block>
        </fo:flow>
      </fo:page-sequence>
    </fo:root>
  </xsl:template>

  <xsl:template match="dmTitle">
    <fo:block font-size="12pt" font-weight="bold" space-after="6pt">
      <xsl:value-of select="techName"/>
      <xsl:text> — </xsl:text>
      <xsl:value-of select="infoName"/>
    </fo:block>
  </xsl:template>

  <xsl:template match="levelledPara/title">
    <fo:block font-size="12pt" font-weight="bold" space-before="6pt" space-after="3pt">
      <xsl:value-of select="."/>
    </fo:block>
  </xsl:template>

  <xsl:template match="para | warningAndCautionPara | notePara">
    <fo:block font-size="12pt" space-after="4pt">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <!-- No box, no shading, no label: just the text, so nothing is silently dropped. -->
  <xsl:template match="warning | caution | note">
    <fo:block space-before="4pt" space-after="4pt">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="randomList">
    <fo:block space-after="4pt"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template match="listItem">
    <fo:block><xsl:apply-templates/></fo:block>
  </xsl:template>

  <!-- The table is emitted as plain blocks: no columns, no rules, no shading. -->
  <xsl:template match="table">
    <fo:block space-before="4pt" space-after="4pt"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template match="table/title">
    <fo:block font-size="12pt" font-weight="bold"><xsl:value-of select="."/></fo:block>
  </xsl:template>

  <xsl:template match="colspec"/>

  <xsl:template match="row">
    <fo:block font-size="12pt"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template match="entry">
    <fo:inline><xsl:apply-templates/><xsl:text>  </xsl:text></fo:inline>
  </xsl:template>

  <xsl:template match="entry/para">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="emphasis">
    <fo:inline><xsl:apply-templates/></fo:inline>
  </xsl:template>

</xsl:stylesheet>

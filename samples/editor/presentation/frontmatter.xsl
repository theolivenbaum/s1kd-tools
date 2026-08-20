<?xml version="1.0" encoding="UTF-8"?>
<!--
  frontmatter.xsl — front matter data module (frontmatter.xsd).

  Front matter is the material at the head of a publication: title page, table
  of contents, list of effective data modules, highlights and change records.
  Each of those is a list with a fixed shape, so each gets a table of its own.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="frontMatter">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="frontMatterTitlePage">
    <fo:block text-align="center" space-before="8mm" space-after="10mm">
      <fo:block font-size="{$fs + 12}pt" font-weight="bold" letter-spacing="2pt" space-after="4mm">
        <xsl:value-of select="productIntroName|pmTitle"/>
      </fo:block>
      <xsl:if test="shortPmTitle">
        <fo:block font-size="{$fs + 4}pt" space-after="3mm">
          <xsl:value-of select="shortPmTitle"/>
        </fo:block>
      </xsl:if>
      <xsl:if test="externalPubCode">
        <fo:block font-size="{$fs + 1}pt" space-after="2mm">
          <xsl:value-of select="externalPubCode"/>
        </fo:block>
      </xsl:if>
      <fo:block font-size="{$fs}pt" space-before="6mm">
        <xsl:value-of select="enterpriseName|responsiblePartnerCompany/enterpriseName"/>
      </fo:block>
      <xsl:apply-templates select="barCode|productIllustration"/>
    </fo:block>
  </xsl:template>

  <xsl:template match="frontMatterTableOfContent">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Table of contents'"/>
    </xsl:call-template>
    <xsl:apply-templates select="frontMatterTitle"/>
    <xsl:apply-templates select="tocEntry"/>
  </xsl:template>

  <xsl:template match="tocEntry">
    <fo:block text-align-last="justify" space-after="0.8mm"
              start-indent="{count(ancestor::tocEntry) * 6}mm">
      <xsl:if test="not(ancestor::tocEntry)">
        <xsl:attribute name="font-weight">bold</xsl:attribute>
        <xsl:attribute name="space-before">2mm</xsl:attribute>
      </xsl:if>
      <xsl:choose>
        <xsl:when test="dmRef/dmRefAddressItems/dmTitle">
          <xsl:value-of select="dmRef/dmRefAddressItems/dmTitle/techName"/>
          <xsl:if test="dmRef/dmRefAddressItems/dmTitle/infoName">
            <xsl:text> — </xsl:text>
            <xsl:value-of select="dmRef/dmRefAddressItems/dmTitle/infoName"/>
          </xsl:if>
        </xsl:when>
        <xsl:otherwise><xsl:value-of select="pmEntryTitle|title"/></xsl:otherwise>
      </xsl:choose>
      <fo:leader leader-pattern="dots" leader-length.minimum="6mm"
                 leader-length.optimum="25mm" leader-length.maximum="100%"/>
      <fo:inline font-size="{$fs-tiny}pt">
        <xsl:if test="dmRef">
          <xsl:call-template name="dm-code-string">
            <xsl:with-param name="c" select="dmRef/dmRefIdent/dmCode"/>
          </xsl:call-template>
        </xsl:if>
      </fo:inline>
    </fo:block>
    <xsl:apply-templates select="tocEntry"/>
  </xsl:template>

  <xsl:template match="frontMatterList">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="frontMatterTitle"><xsl:value-of select="frontMatterTitle"/></xsl:when>
          <xsl:when test="@type = 'loedm'">List of effective data modules</xsl:when>
          <xsl:otherwise>List</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.40}mm"/>
      <fo:table-column column-width="{$body-w * 0.38}mm"/>
      <fo:table-column column-width="{$body-w * 0.10}mm"/>
      <fo:table-column column-width="{$body-w * 0.12}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="fm-head"><xsl:with-param name="t" select="'DATA MODULE CODE'"/></xsl:call-template>
          <xsl:call-template name="fm-head"><xsl:with-param name="t" select="'TITLE'"/></xsl:call-template>
          <xsl:call-template name="fm-head"><xsl:with-param name="t" select="'ISSUE'"/></xsl:call-template>
          <xsl:call-template name="fm-head"><xsl:with-param name="t" select="'DATE'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select="frontMatterListItem" mode="fmlist"/>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template name="fm-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template match="frontMatterListItem" mode="fmlist">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-size="{$fs-tiny}pt">
          <xsl:if test="dmRef">
            <xsl:call-template name="dm-code-string">
              <xsl:with-param name="c" select="dmRef/dmRefIdent/dmCode"/>
            </xsl:call-template>
          </xsl:if>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:value-of select="dmRef/dmRefAddressItems/dmTitle/techName"/>
          <xsl:if test="dmRef/dmRefAddressItems/dmTitle/infoName">
            <xsl:text> — </xsl:text>
            <xsl:value-of select="dmRef/dmRefAddressItems/dmTitle/infoName"/>
          </xsl:if>
          <xsl:if test="reasonForUpdate">
            <fo:block font-size="{$fs-tiny}pt" color="#444444" space-before="0.5mm">
              <xsl:value-of select="reasonForUpdate/simplePara"/>
            </fo:block>
          </xsl:if>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block text-align="center">
          <xsl:value-of select="dmRef/dmRefIdent/issueInfo/@issueNumber"/>
          <xsl:if test="dmRef/dmRefIdent/issueInfo/@inWork">
            <xsl:text>-</xsl:text>
            <xsl:value-of select="dmRef/dmRefIdent/issueInfo/@inWork"/>
          </xsl:if>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block text-align="center" font-size="{$fs-tiny}pt">
          <xsl:call-template name="format-date">
            <xsl:with-param name="date" select="dmRef/dmRefAddressItems/issueDate"/>
          </xsl:call-template>
        </fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <xsl:template match="frontMatterTitle">
    <fo:block font-weight="bold" space-after="1.5mm"><xsl:apply-templates/></fo:block>
  </xsl:template>

</xsl:stylesheet>

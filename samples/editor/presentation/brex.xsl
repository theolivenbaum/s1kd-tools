<?xml version="1.0" encoding="UTF-8"?>
<!--
  brex.xsl — business rules exchange data module (brex.xsd).

  A BREX data module is a machine-readable rule set, and the useful printed form
  of it is a rule table: the object path the rule constrains, whether the object
  is allowed, the values it may take and the message an author is shown when the
  rule fails. Context rules, SNS rules and notation rules each get their own
  table.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="brex">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="contextRules">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="@rulesContext">
            <xsl:text>Context rules — </xsl:text>
            <xsl:value-of select="@rulesContext"/>
          </xsl:when>
          <xsl:otherwise>Context rules</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="structureObjectRuleGroup">
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" space-after="3mm">
      <fo:table-column column-width="{$body-w * 0.36}mm"/>
      <fo:table-column column-width="{$body-w * 0.10}mm"/>
      <fo:table-column column-width="{$body-w * 0.54}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="brex-head"><xsl:with-param name="t" select="'OBJECT PATH'"/></xsl:call-template>
          <xsl:call-template name="brex-head"><xsl:with-param name="t" select="'ALLOWED'"/></xsl:call-template>
          <xsl:call-template name="brex-head"><xsl:with-param name="t" select="'RULE'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select="structureObjectRule" mode="brex"/>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template name="brex-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template match="structureObjectRule" mode="brex">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-family="{$mono-font-family}" font-size="{$fs-tiny}pt">
          <xsl:call-template name="path-lines">
            <xsl:with-param name="path" select="objectPath"/>
          </xsl:call-template>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block text-align="center">
          <xsl:choose>
            <xsl:when test="objectPath/@allowedObjectFlag = '0'">no</xsl:when>
            <xsl:when test="objectPath/@allowedObjectFlag = '1'">yes</xsl:when>
            <xsl:when test="objectPath/@allowedObjectFlag = '2'">yes*</xsl:when>
            <xsl:otherwise>—</xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:value-of select="objectUse"/></fo:block>
        <xsl:if test="objectValue">
          <fo:block font-size="{$fs-tiny}pt" space-before="0.8mm">
            <fo:inline font-weight="bold">Values: </fo:inline>
            <xsl:for-each select="objectValue">
              <xsl:if test="position() &gt; 1">, </xsl:if>
              <xsl:value-of select="@valueAllowed"/>
              <xsl:if test="normalize-space(.) != ''">
                <xsl:text> (</xsl:text><xsl:value-of select="normalize-space(.)"/><xsl:text>)</xsl:text>
              </xsl:if>
            </xsl:for-each>
          </fo:block>
        </xsl:if>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <xsl:template match="snsRules">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Standard numbering system'"/>
    </xsl:call-template>
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.2}mm"/>
      <fo:table-column column-width="{$body-w * 0.8}mm"/>
      <fo:table-body>
        <xsl:for-each select=".//snsCode">
          <fo:table-row>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block start-indent="{count(ancestor::snsSubSystem|ancestor::snsSubSubSystem|ancestor::snsAssy) * 3}mm">
                <xsl:value-of select="."/>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block><xsl:value-of select="following-sibling::snsTitle[1]"/></fo:block>
            </fo:table-cell>
          </fo:table-row>
        </xsl:for-each>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template match="notationRules">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Notation rules'"/>
    </xsl:call-template>
    <xsl:for-each select="notationRule">
      <fo:block space-after="1.5mm" start-indent="6mm">
        <fo:inline font-weight="bold"><xsl:value-of select="notationName"/></fo:inline>
        <xsl:text> — </xsl:text>
        <xsl:value-of select="objectUse"/>
      </fo:block>
    </xsl:for-each>
  </xsl:template>

</xsl:stylesheet>

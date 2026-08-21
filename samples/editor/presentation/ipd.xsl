<?xml version="1.0" encoding="UTF-8"?>
<!--
  ipd.xsl — illustrated parts data module (ipd.xsd).

  An illustrated parts catalogue is the exploded view followed by the parts
  list keyed to it. The list is printed in the classic column order — figure
  and item number, part number, manufacturer (CAGE) code, nomenclature indented
  by indenture level, units per assembly and effectivity — so an item on the
  illustration can be found by eye.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="illustratedPartsCatalog">
    <xsl:apply-templates select="figure|foldout"/>

    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Parts list'"/>
    </xsl:call-template>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.11}mm"/>
      <fo:table-column column-width="{$body-w * 0.20}mm"/>
      <fo:table-column column-width="{$body-w * 0.09}mm"/>
      <fo:table-column column-width="{$body-w * 0.38}mm"/>
      <fo:table-column column-width="{$body-w * 0.08}mm"/>
      <fo:table-column column-width="{$body-w * 0.14}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="ipd-head"><xsl:with-param name="t" select="'FIG-ITEM'"/></xsl:call-template>
          <xsl:call-template name="ipd-head"><xsl:with-param name="t" select="'PART NUMBER'"/></xsl:call-template>
          <xsl:call-template name="ipd-head"><xsl:with-param name="t" select="'CAGE'"/></xsl:call-template>
          <xsl:call-template name="ipd-head"><xsl:with-param name="t" select="'NOMENCLATURE'"/></xsl:call-template>
          <xsl:call-template name="ipd-head"><xsl:with-param name="t" select="'UNITS'"/></xsl:call-template>
          <xsl:call-template name="ipd-head"><xsl:with-param name="t" select="'EFFECTIVITY'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select="catalogSeqNumber" mode="ipd"/>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template name="ipd-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template match="catalogSeqNumber" mode="ipd">
    <xsl:apply-templates select="itemSeqNumber" mode="ipd"/>
  </xsl:template>

  <xsl:template match="itemSeqNumber" mode="ipd">
    <xsl:variable name="csn" select="parent::catalogSeqNumber"/>
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1mm">
        <fo:block>
          <xsl:value-of select="$csn/@figureNumber"/>
          <xsl:if test="$csn/@figureNumberVariant"><xsl:value-of select="$csn/@figureNumberVariant"/></xsl:if>
          <xsl:text>-</xsl:text>
          <xsl:value-of select="$csn/@item"/>
          <xsl:value-of select="@itemSeqNumberValue"/>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1mm">
        <fo:block><xsl:value-of select="partRef/@partNumberValue"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1mm">
        <fo:block><xsl:value-of select="partRef/@manufacturerCodeValue"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1mm">
        <!-- Indenture is what makes a parts list readable: each level of the
             breakdown is stepped in by one em, as in a printed IPC. -->
        <fo:block start-indent="{(number($csn/@indenture) - 1) * 4}mm">
          <xsl:if test="not(number($csn/@indenture) &gt; 0)">
            <xsl:attribute name="start-indent">0mm</xsl:attribute>
          </xsl:if>
          <xsl:choose>
            <xsl:when test="partSegment/itemIdentData/descrForPart">
              <xsl:value-of select="partSegment/itemIdentData/descrForPart"/>
            </xsl:when>
            <xsl:when test="partSegment/itemIdentData/name">
              <xsl:value-of select="partSegment/itemIdentData/name"/>
            </xsl:when>
            <xsl:otherwise>—</xsl:otherwise>
          </xsl:choose>
          <xsl:if test="locationRcmdSegment/locationRcmd/remarks">
            <fo:block font-size="{$fs-tiny}pt" color="#444444">
              <xsl:value-of select="locationRcmdSegment/locationRcmd/remarks"/>
            </fo:block>
          </xsl:if>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1mm">
        <fo:block text-align="center">
          <xsl:choose>
            <xsl:when test="quantityPerNextHigherAssy">
              <xsl:value-of select="quantityPerNextHigherAssy"/>
            </xsl:when>
            <xsl:otherwise>—</xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1mm">
        <fo:block font-size="{$fs-tiny}pt">
          <xsl:choose>
            <xsl:when test="@applicRefId">
              <xsl:value-of select="//referencedApplicGroup/applic[@id = current()/@applicRefId]/displayText/simplePara"/>
            </xsl:when>
            <xsl:otherwise>ALL</xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

</xsl:stylesheet>

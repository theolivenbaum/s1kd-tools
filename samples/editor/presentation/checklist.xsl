<?xml version="1.0" encoding="UTF-8"?>
<!--
  checklist.xsl — checklist data module (checklist.xsd).

  Printed as a working check sheet: one numbered row per item, the check text,
  the limits it is checked against and a blank column the technician signs.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="checkList">
    <xsl:apply-templates select="*[not(self::checkListInfo)]"/>
    <xsl:apply-templates select="checkListInfo"/>
  </xsl:template>

  <xsl:template match="checkListInfo">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="title"><xsl:value-of select="title"/></xsl:when>
          <xsl:otherwise>Check list</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.07}mm"/>
      <fo:table-column column-width="{$body-w * 0.50}mm"/>
      <fo:table-column column-width="{$body-w * 0.28}mm"/>
      <fo:table-column column-width="{$body-w * 0.15}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="cl-head"><xsl:with-param name="t" select="'No.'"/></xsl:call-template>
          <xsl:call-template name="cl-head"><xsl:with-param name="t" select="'CHECK'"/></xsl:call-template>
          <xsl:call-template name="cl-head"><xsl:with-param name="t" select="'LIMITS / CRITERIA'"/></xsl:call-template>
          <xsl:call-template name="cl-head"><xsl:with-param name="t" select="'RESULT'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select="checkListItems/checkListItem" mode="checklist"/>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template name="cl-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template match="checkListItem" mode="checklist">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block text-align="center"><xsl:number count="checkListItem" format="1"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:call-template name="applicability-annotation"/>
          <xsl:apply-templates select="*[not(self::checkListItemLimits|self::checkListItemResult)]"/>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:choose>
            <xsl:when test="checkListItemLimits">
              <xsl:apply-templates select="checkListItemLimits/node()"/>
            </xsl:when>
            <xsl:otherwise>—</xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
      <!-- Deliberately blank: the result column is filled in by hand. -->
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block> </fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

</xsl:stylesheet>

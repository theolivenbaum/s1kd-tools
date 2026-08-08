<?xml version="1.0" encoding="UTF-8"?>
<!--
  condcrossreftable.xsl — conditions cross-reference table (condcrossreftable.xsd).

  The CCT declares the condition types a project may write applicability
  against — modifications, service bulletins, operational states — and the
  conditions of each type. Both lists print as dictionaries.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="appliccrossreftable.xsl"/>

  <xsl:template match="condCrossRefTable">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="condTypeList">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Condition types'"/>
    </xsl:call-template>
    <xsl:call-template name="attribute-value-table">
      <xsl:with-param name="items" select="condType"/>
      <xsl:with-param name="idAttribute" select="'condType'"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="condList">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Conditions'"/>
    </xsl:call-template>
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.22}mm"/>
      <fo:table-column column-width="{$body-w * 0.20}mm"/>
      <fo:table-column column-width="{$body-w * 0.58}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="act-head"><xsl:with-param name="t" select="'IDENTIFIER'"/></xsl:call-template>
          <xsl:call-template name="act-head"><xsl:with-param name="t" select="'TYPE'"/></xsl:call-template>
          <xsl:call-template name="act-head"><xsl:with-param name="t" select="'CONDITION'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:for-each select="cond">
          <fo:table-row>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block font-family="{$mono-font-family}" font-size="{$fs-tiny}pt">
                <xsl:value-of select="@id"/>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block font-size="{$fs-tiny}pt"><xsl:value-of select="@condTypeRefId"/></fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block><xsl:value-of select="name"/></fo:block>
              <xsl:if test="descr">
                <fo:block font-size="{$fs-tiny}pt" color="#444444" space-before="0.5mm">
                  <xsl:value-of select="descr"/>
                </fo:block>
              </xsl:if>
            </fo:table-cell>
          </fo:table-row>
        </xsl:for-each>
      </fo:table-body>
    </fo:table>
  </xsl:template>

</xsl:stylesheet>

<?xml version="1.0" encoding="UTF-8"?>
<!--
  appliccrossreftable.xsl — applicability cross-reference table (appliccrossreftable.xsd).

  The ACT declares the product attributes a project may write applicability
  against, and points at the conditions and products cross-reference tables. It
  prints as the attribute dictionary: identifier, name, type and permitted
  values.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="applicCrossRefTable">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="productAttributeList">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Product attributes'"/>
    </xsl:call-template>
    <xsl:call-template name="attribute-value-table">
      <xsl:with-param name="items" select="productAttribute"/>
      <xsl:with-param name="idAttribute" select="'productAttribute'"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="condCrossRefTableRef|productCrossRefTableRef">
    <fo:block space-before="2mm" space-after="2mm">
      <fo:inline font-weight="bold">
        <xsl:call-template name="camel-to-words">
          <xsl:with-param name="text" select="local-name()"/>
        </xsl:call-template>
        <xsl:text>: </xsl:text>
      </fo:inline>
      <xsl:apply-templates select="dmRef"/>
    </fo:block>
  </xsl:template>

  <!--
    Shared by the three cross-reference tables: identifier, display name, value
    data type and the enumerated values that are allowed.
  -->
  <xsl:template name="attribute-value-table">
    <xsl:param name="items"/>
    <xsl:param name="idAttribute"/>
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" space-after="3mm">
      <fo:table-column column-width="{$body-w * 0.2}mm"/>
      <fo:table-column column-width="{$body-w * 0.3}mm"/>
      <fo:table-column column-width="{$body-w * 0.15}mm"/>
      <fo:table-column column-width="{$body-w * 0.35}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="act-head"><xsl:with-param name="t" select="'IDENTIFIER'"/></xsl:call-template>
          <xsl:call-template name="act-head"><xsl:with-param name="t" select="'NAME'"/></xsl:call-template>
          <xsl:call-template name="act-head"><xsl:with-param name="t" select="'TYPE'"/></xsl:call-template>
          <xsl:call-template name="act-head"><xsl:with-param name="t" select="'PERMITTED VALUES'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:for-each select="$items">
          <fo:table-row>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block font-family="{$mono-font-family}" font-size="{$fs-tiny}pt">
                <xsl:value-of select="@id"/>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block><xsl:value-of select="name|displayName"/></fo:block>
              <xsl:if test="descr">
                <fo:block font-size="{$fs-tiny}pt" color="#444444" space-before="0.5mm">
                  <xsl:value-of select="descr"/>
                </fo:block>
              </xsl:if>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block><xsl:value-of select="@valueDataType|@condTypeValueDataType"/></fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block>
                <xsl:choose>
                  <xsl:when test="enumeration">
                    <xsl:for-each select="enumeration">
                      <xsl:if test="position() &gt; 1">; </xsl:if>
                      <xsl:value-of select="@applicPropertyValues|@enumerationValues"/>
                    </xsl:for-each>
                  </xsl:when>
                  <xsl:when test="@valuePattern"><xsl:value-of select="@valuePattern"/></xsl:when>
                  <xsl:otherwise>any</xsl:otherwise>
                </xsl:choose>
              </fo:block>
            </fo:table-cell>
          </fo:table-row>
        </xsl:for-each>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template name="act-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

</xsl:stylesheet>

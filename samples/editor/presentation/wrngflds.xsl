<?xml version="1.0" encoding="UTF-8"?>
<!--
  wrngflds.xsl — wiring fields data module (wrngflds.xsd).

  Wiring fields declare the columns a project's wiring data uses: the field
  name, what it holds and how it is formatted. The printed form is the field
  dictionary — a table an author consults while filling in wiring data.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="wiringFields">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Wiring data field definitions'"/>
    </xsl:call-template>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.20}mm"/>
      <fo:table-column column-width="{$body-w * 0.42}mm"/>
      <fo:table-column column-width="{$body-w * 0.20}mm"/>
      <fo:table-column column-width="{$body-w * 0.18}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="wf-head"><xsl:with-param name="t" select="'FIELD'"/></xsl:call-template>
          <xsl:call-template name="wf-head"><xsl:with-param name="t" select="'CONTENT'"/></xsl:call-template>
          <xsl:call-template name="wf-head"><xsl:with-param name="t" select="'FORMAT'"/></xsl:call-template>
          <xsl:call-template name="wf-head"><xsl:with-param name="t" select="'USE'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select=".//wiringField|.//field" mode="fields"/>
      </fo:table-body>
    </fo:table>

    <xsl:apply-templates select="*[not(self::wiringField|self::field|self::fieldGroup)]"/>
  </xsl:template>

  <xsl:template name="wf-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template match="wiringField|field" mode="fields">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-family="{$mono-font-family}" font-size="{$fs-tiny}pt">
          <xsl:value-of select="@fieldIdent|fieldIdent|@name"/>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:value-of select="fieldName|name|descr"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:value-of select="@fieldFormat|fieldFormat"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:value-of select="@fieldUse|fieldUse"/></fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

</xsl:stylesheet>

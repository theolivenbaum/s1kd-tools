<?xml version="1.0" encoding="UTF-8"?>
<!--
  prdcrossreftable.xsl — products cross-reference table (prdcrossreftable.xsd).

  The PCT lists the individual products a project covers and the value each one
  has for every product attribute declared in the applicability cross-reference
  table. Printed as one block per product with its attribute values, which is
  how an engineer checks whether a data module applies to a given aircraft.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="appliccrossreftable.xsl"/>

  <xsl:template match="productCrossRefTable">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="product">
    <fo:block space-before="3mm" keep-together.within-page="always">
      <fo:block font-weight="bold" background-color="{$shade}" border="{$cell-rule}"
                padding="1.2mm" space-after="1.5mm">
        <xsl:text>PRODUCT </xsl:text>
        <xsl:value-of select="@id"/>
        <xsl:if test="name">
          <xsl:text> — </xsl:text><xsl:value-of select="name"/>
        </xsl:if>
      </fo:block>
      <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
                font-size="{$fs-small}pt">
        <fo:table-column column-width="{$body-w * 0.4}mm"/>
        <fo:table-column column-width="{$body-w * 0.6}mm"/>
        <fo:table-body>
          <xsl:for-each select="assign">
            <xsl:call-template name="kv-row">
              <xsl:with-param name="label" select="@applicPropertyIdent"/>
              <xsl:with-param name="value" select="@applicPropertyValue|@applicPropertyValues"/>
            </xsl:call-template>
          </xsl:for-each>
        </fo:table-body>
      </fo:table>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

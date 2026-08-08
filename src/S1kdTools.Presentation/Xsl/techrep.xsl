<?xml version="1.0" encoding="UTF-8"?>
<!--
  techrep.xsl — technical repository data module (techrep.xsd).

  A technical repository holds the reusable technical facts of a project: part
  specifications, zones, access points, circuit breakers, functional items. Each
  repository is printed as its own table of specifications keyed by identifier.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="techRepository">
    <xsl:apply-templates/>
  </xsl:template>

  <!-- Every *Repository child is presented the same way: a heading from the
       element name, then one specification block per entry. Matched on the
       children only — techRepository itself also ends in "Repository". -->
  <xsl:template match="techRepository/*[substring(local-name(), string-length(local-name()) - 9) = 'Repository']">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text">
        <xsl:call-template name="camel-to-words">
          <xsl:with-param name="text" select="local-name()"/>
        </xsl:call-template>
      </xsl:with-param>
    </xsl:call-template>
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="partSpec">
    <fo:block space-before="3mm" keep-together.within-page="always">
      <fo:block font-weight="bold" background-color="{$shade}" border="{$cell-rule}"
                padding="1.2mm" space-after="1.5mm">
        <xsl:value-of select="partIdent/@partNumberValue"/>
        <xsl:if test="partIdent/@manufacturerCodeValue">
          <xsl:text> · CAGE </xsl:text>
          <xsl:value-of select="partIdent/@manufacturerCodeValue"/>
        </xsl:if>
        <xsl:if test="itemIdentData/descrForPart">
          <xsl:text> — </xsl:text>
          <xsl:value-of select="itemIdentData/descrForPart"/>
        </xsl:if>
      </fo:block>
      <fo:block start-indent="4mm">
        <xsl:apply-templates select="*[not(self::partIdent)]"/>
      </fo:block>
    </fo:block>
  </xsl:template>

  <xsl:template match="itemIdentData">
    <fo:table table-layout="fixed" width="{$body-w - 4}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" space-after="2mm">
      <fo:table-column column-width="{($body-w - 4) * 0.3}mm"/>
      <fo:table-column column-width="{($body-w - 4) * 0.7}mm"/>
      <fo:table-body>
        <xsl:for-each select="*">
          <xsl:call-template name="kv-row">
            <xsl:with-param name="label">
              <xsl:call-template name="camel-to-words">
                <xsl:with-param name="text" select="local-name()"/>
              </xsl:call-template>
            </xsl:with-param>
            <xsl:with-param name="value" select="normalize-space(.)"/>
          </xsl:call-template>
        </xsl:for-each>
      </fo:table-body>
    </fo:table>
  </xsl:template>

</xsl:stylesheet>

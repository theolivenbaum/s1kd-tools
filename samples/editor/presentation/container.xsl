<?xml version="1.0" encoding="UTF-8"?>
<!--
  container.xsl — container data module (container.xsd).

  A container carries no content of its own: it stands in for a set of data
  modules that differ only in language or configuration, and points at them.
  Printed, it is a short notice plus the table of what it contains.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="container">
    <fo:block space-before="3mm" space-after="3mm" font-style="italic">
      This is a container data module. The content is held in the data modules
      listed below; select the one that matches the configuration and language
      you need.
    </fo:block>

    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Contained data modules'"/>
    </xsl:call-template>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.44}mm"/>
      <fo:table-column column-width="{$body-w * 0.38}mm"/>
      <fo:table-column column-width="{$body-w * 0.18}mm"/>
      <fo:table-header>
        <fo:table-row>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold" font-size="{$fs-tiny}pt">DATA MODULE CODE</fo:block>
          </fo:table-cell>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold" font-size="{$fs-tiny}pt">TITLE</fo:block>
          </fo:table-cell>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold" font-size="{$fs-tiny}pt">LANGUAGE</fo:block>
          </fo:table-cell>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:for-each select=".//dmRef">
          <fo:table-row>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block font-size="{$fs-tiny}pt">
                <xsl:call-template name="dm-code-string">
                  <xsl:with-param name="c" select="dmRefIdent/dmCode"/>
                </xsl:call-template>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block>
                <xsl:value-of select="dmRefAddressItems/dmTitle/techName"/>
                <xsl:if test="dmRefAddressItems/dmTitle/infoName">
                  <xsl:text> — </xsl:text>
                  <xsl:value-of select="dmRefAddressItems/dmTitle/infoName"/>
                </xsl:if>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block text-align="center">
                <xsl:choose>
                  <xsl:when test="dmRefIdent/language">
                    <xsl:value-of select="dmRefIdent/language/@languageIsoCode"/>
                    <xsl:text>-</xsl:text>
                    <xsl:value-of select="dmRefIdent/language/@countryIsoCode"/>
                  </xsl:when>
                  <xsl:otherwise>—</xsl:otherwise>
                </xsl:choose>
              </fo:block>
            </fo:table-cell>
          </fo:table-row>
        </xsl:for-each>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <!-- The reference list is the container's payload; it is printed above. -->
  <xsl:template match="container/refs"/>

</xsl:stylesheet>

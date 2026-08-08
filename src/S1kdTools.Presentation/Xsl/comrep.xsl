<?xml version="1.0" encoding="UTF-8"?>
<!--
  comrep.xsl — common information repository data module (comrep.xsd).

  A common repository holds the warnings, cautions, tools, supplies, parts,
  zones, access points and enterprises that data modules reference instead of
  repeating. Each repository prints as a table keyed by the identifier the
  referring data modules cite, so a repository entry can be looked up from a
  warningRef or supplyRef in any other module.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="commonRepository">
    <xsl:apply-templates/>
  </xsl:template>

  <!-- Each *Repository child of the common repository, not the commonRepository
       element itself — its own name ends in "Repository" too. -->
  <xsl:template match="commonRepository/*[substring(local-name(), string-length(local-name()) - 9) = 'Repository']">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text">
        <xsl:call-template name="camel-to-words">
          <xsl:with-param name="text" select="local-name()"/>
        </xsl:call-template>
      </xsl:with-param>
    </xsl:call-template>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" space-after="3mm">
      <fo:table-column column-width="{$body-w * 0.22}mm"/>
      <fo:table-column column-width="{$body-w * 0.78}mm"/>
      <fo:table-header>
        <fo:table-row>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold" font-size="{$fs-tiny}pt">IDENTIFIER</fo:block>
          </fo:table-cell>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold" font-size="{$fs-tiny}pt">ENTRY</fo:block>
          </fo:table-cell>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select="*" mode="repository"/>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template match="*" mode="repository">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-family="{$mono-font-family}" font-size="{$fs-tiny}pt">
          <xsl:call-template name="repository-identifier"/>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:apply-templates/></fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <!--
    Repository entries key on an ident number whose attribute name changes with
    the repository (warningIdentNumber, supplyIdentNumber, …), so take whichever
    "*IdentNumber" attribute or child the entry carries.
  -->
  <xsl:template name="repository-identifier">
    <xsl:choose>
      <xsl:when test="@*[substring(local-name(), string-length(local-name()) - 10) = 'IdentNumber']">
        <xsl:value-of select="@*[substring(local-name(), string-length(local-name()) - 10) = 'IdentNumber'][1]"/>
      </xsl:when>
      <xsl:when test="*[substring(local-name(), string-length(local-name()) - 10) = 'IdentNumber']">
        <xsl:value-of select="*[substring(local-name(), string-length(local-name()) - 10) = 'IdentNumber'][1]"/>
      </xsl:when>
      <xsl:when test="@id"><xsl:value-of select="@id"/></xsl:when>
      <xsl:otherwise>
        <xsl:number count="*" format="1"/>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <!-- Inside a repository table the boxed warning/caution presentation would
       fight the table grid, so the text is set plainly with a bold label. -->
  <xsl:template match="warningSpec|cautionSpec" mode="repository">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-family="{$mono-font-family}" font-size="{$fs-tiny}pt">
          <xsl:value-of select="@warningIdentNumber|@cautionIdentNumber|warningIdentNumber|cautionIdentNumber"/>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-weight="bold" space-after="0.8mm">
          <xsl:choose>
            <xsl:when test="self::warningSpec">WARNING</xsl:when>
            <xsl:otherwise>CAUTION</xsl:otherwise>
          </xsl:choose>
        </fo:block>
        <xsl:apply-templates select="warningAndCautionPara|para"/>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <xsl:template match="warningAndCautionPara|para" mode="repository">
    <xsl:apply-templates select="."/>
  </xsl:template>

</xsl:stylesheet>

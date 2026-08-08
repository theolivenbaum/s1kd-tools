<?xml version="1.0" encoding="UTF-8"?>
<!--
  dml.xsl — data management list (dml.xsd).

  A DML is the register of the objects a project has produced or delivered. It
  prints as one row per entry: the object's code, its title, issue, issue type
  and security classification — the columns a data manager reconciles against.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="dmlContent">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'List entries'"/>
    </xsl:call-template>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.06}mm"/>
      <fo:table-column column-width="{$body-w * 0.36}mm"/>
      <fo:table-column column-width="{$body-w * 0.34}mm"/>
      <fo:table-column column-width="{$body-w * 0.10}mm"/>
      <fo:table-column column-width="{$body-w * 0.14}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="dml-head"><xsl:with-param name="t" select="'No.'"/></xsl:call-template>
          <xsl:call-template name="dml-head"><xsl:with-param name="t" select="'OBJECT CODE'"/></xsl:call-template>
          <xsl:call-template name="dml-head"><xsl:with-param name="t" select="'TITLE'"/></xsl:call-template>
          <xsl:call-template name="dml-head"><xsl:with-param name="t" select="'ISSUE'"/></xsl:call-template>
          <xsl:call-template name="dml-head"><xsl:with-param name="t" select="'TYPE / SEC'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select="dmlEntry" mode="dml"/>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template name="dml-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template match="dmlEntry" mode="dml">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block text-align="center"><xsl:number count="dmlEntry" format="1"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-size="{$fs-tiny}pt">
          <xsl:choose>
            <xsl:when test="dmRef">
              <xsl:call-template name="dm-code-string">
                <xsl:with-param name="c" select="dmRef/dmRefIdent/dmCode"/>
              </xsl:call-template>
            </xsl:when>
            <xsl:when test="pmRef">
              <xsl:call-template name="pm-code-string">
                <xsl:with-param name="c" select="pmRef/pmRefIdent/pmCode"/>
              </xsl:call-template>
            </xsl:when>
            <xsl:otherwise><xsl:value-of select="@dmlEntryType"/></xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:value-of select="dmRef/dmRefAddressItems/dmTitle/techName"/>
          <xsl:if test="dmRef/dmRefAddressItems/dmTitle/infoName">
            <xsl:text> — </xsl:text>
            <xsl:value-of select="dmRef/dmRefAddressItems/dmTitle/infoName"/>
          </xsl:if>
          <xsl:value-of select="pmRef/pmRefAddressItems/pmTitle"/>
          <xsl:if test="remarks">
            <fo:block font-size="{$fs-tiny}pt" color="#444444" space-before="0.5mm">
              <xsl:value-of select="remarks/simplePara"/>
            </fo:block>
          </xsl:if>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block text-align="center">
          <xsl:value-of select="dmRef/dmRefIdent/issueInfo/@issueNumber|pmRef/pmRefIdent/issueInfo/@issueNumber"/>
          <xsl:text>-</xsl:text>
          <xsl:value-of select="dmRef/dmRefIdent/issueInfo/@inWork|pmRef/pmRefIdent/issueInfo/@inWork"/>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-size="{$fs-tiny}pt">
          <xsl:value-of select="@issueType"/>
          <xsl:if test="security/@securityClassification">
            <xsl:text> / </xsl:text>
            <xsl:value-of select="security/@securityClassification"/>
          </xsl:if>
        </fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

</xsl:stylesheet>

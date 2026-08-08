<?xml version="1.0" encoding="UTF-8"?>
<!--
  wrngdata.xsl — wiring data data module (wrngdata.xsd).

  Wiring data is tabular by nature — wires, harnesses, connectors and the pins
  they land on — and that is how a wiring manual prints it: one table per data
  group, fixed column order, monospaced identifiers so a wire number can be
  matched against the loom label by eye.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="wiringData">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="wireData">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Wire list'"/>
    </xsl:call-template>
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.16}mm"/>
      <fo:table-column column-width="{$body-w * 0.10}mm"/>
      <fo:table-column column-width="{$body-w * 0.12}mm"/>
      <fo:table-column column-width="{$body-w * 0.24}mm"/>
      <fo:table-column column-width="{$body-w * 0.24}mm"/>
      <fo:table-column column-width="{$body-w * 0.14}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'WIRE No.'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'GAUGE'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'TYPE'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'FROM'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'TO'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'LENGTH'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select="wire" mode="wiring"/>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template name="w-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template match="wire" mode="wiring">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-family="{$mono-font-family}" font-size="{$fs-tiny}pt">
          <xsl:value-of select="@wireIdent|wireIdent"/>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block text-align="center"><xsl:value-of select="@wireGauge|wireGauge"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:value-of select="@wireType|wireType"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:call-template name="termination">
          <xsl:with-param name="node" select="wireEndFrom|wireFrom"/>
        </xsl:call-template></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:call-template name="termination">
          <xsl:with-param name="node" select="wireEndTo|wireTo"/>
        </xsl:call-template></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block text-align="center"><xsl:value-of select="@wireLength|wireLength"/></fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <!-- A wire end is a connector plus the contact it lands on. -->
  <xsl:template name="termination">
    <xsl:param name="node"/>
    <xsl:choose>
      <xsl:when test="$node/@connectorIdent">
        <xsl:value-of select="$node/@connectorIdent"/>
        <xsl:if test="$node/@contactIdent">
          <xsl:text>-</xsl:text>
          <xsl:value-of select="$node/@contactIdent"/>
        </xsl:if>
      </xsl:when>
      <xsl:when test="$node"><xsl:value-of select="normalize-space($node)"/></xsl:when>
      <xsl:otherwise>—</xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="harnessData">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Harness list'"/>
    </xsl:call-template>
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.2}mm"/>
      <fo:table-column column-width="{$body-w * 0.5}mm"/>
      <fo:table-column column-width="{$body-w * 0.3}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'HARNESS'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'DESIGNATION'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'ZONE'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:for-each select="harness">
          <fo:table-row>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block font-family="{$mono-font-family}" font-size="{$fs-tiny}pt">
                <xsl:value-of select="@harnessIdent"/>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block><xsl:value-of select="name|harnessName"/></fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block><xsl:value-of select="@zoneIdent|zoneRef/@zoneNumber"/></fo:block>
            </fo:table-cell>
          </fo:table-row>
        </xsl:for-each>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template match="connectorData">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Connector list'"/>
    </xsl:call-template>
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.18}mm"/>
      <fo:table-column column-width="{$body-w * 0.34}mm"/>
      <fo:table-column column-width="{$body-w * 0.28}mm"/>
      <fo:table-column column-width="{$body-w * 0.20}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'CONNECTOR'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'DESIGNATION'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'PART NUMBER'"/></xsl:call-template>
          <xsl:call-template name="w-head"><xsl:with-param name="t" select="'CONTACTS'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:for-each select="connector">
          <fo:table-row>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block font-family="{$mono-font-family}" font-size="{$fs-tiny}pt">
                <xsl:value-of select="@connectorIdent"/>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block><xsl:value-of select="name|connectorName"/></fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block><xsl:value-of select="partRef/@partNumberValue|@partNumberValue"/></fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block text-align="center"><xsl:value-of select="@contactQuantity|contactQuantity"/></fo:block>
            </fo:table-cell>
          </fo:table-row>
        </xsl:for-each>
      </fo:table-body>
    </fo:table>
  </xsl:template>

</xsl:stylesheet>

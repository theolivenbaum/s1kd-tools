<?xml version="1.0" encoding="UTF-8"?>
<!--
  ddn.xsl — data dispatch note (ddn.xsd).

  A dispatch note travels with a delivery of CSDB objects, so it prints as a
  despatch document: who sent it and to whom, side by side, then the delivery
  list, then the authorization line.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <!-- Addresses live in the ident section, which the title block otherwise
       swallows; print them before the delivery list. -->
  <xsl:template name="document-body">
    <xsl:call-template name="dispatch-addresses"/>
    <xsl:apply-templates select="/ddn/ddnContent"/>
    <xsl:call-template name="authorization"/>
  </xsl:template>

  <xsl:template name="dispatch-addresses">
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" space-after="4mm">
      <fo:table-column column-width="{$body-w * 0.5}mm"/>
      <fo:table-column column-width="{$body-w * 0.5}mm"/>
      <fo:table-header>
        <fo:table-row>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold">DISPATCHED FROM</fo:block>
          </fo:table-cell>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold">DISPATCHED TO</fo:block>
          </fo:table-cell>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <fo:table-row>
          <fo:table-cell border="{$cell-rule}" padding="1.5mm">
            <xsl:call-template name="dispatch-address">
              <xsl:with-param name="node" select="/ddn/identAndStatusSection/ddnAddress/ddnAddressItems/dispatchFrom/dispatchAddress"/>
            </xsl:call-template>
          </fo:table-cell>
          <fo:table-cell border="{$cell-rule}" padding="1.5mm">
            <xsl:call-template name="dispatch-address">
              <xsl:with-param name="node" select="/ddn/identAndStatusSection/ddnAddress/ddnAddressItems/dispatchTo/dispatchAddress"/>
            </xsl:call-template>
          </fo:table-cell>
        </fo:table-row>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template name="dispatch-address">
    <xsl:param name="node"/>
    <fo:block font-weight="bold"><xsl:value-of select="$node/enterprise/enterpriseName"/></fo:block>
    <xsl:for-each select="$node/address/*">
      <fo:block><xsl:value-of select="."/></fo:block>
    </xsl:for-each>
  </xsl:template>

  <xsl:template match="ddnContent">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Delivery list'"/>
    </xsl:call-template>
    <xsl:apply-templates select="deliveryList"/>
    <xsl:apply-templates select="*[not(self::deliveryList)]"/>
  </xsl:template>

  <xsl:template match="deliveryList">
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.06}mm"/>
      <fo:table-column column-width="{$body-w * 0.48}mm"/>
      <fo:table-column column-width="{$body-w * 0.32}mm"/>
      <fo:table-column column-width="{$body-w * 0.14}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="ddn-head"><xsl:with-param name="t" select="'No.'"/></xsl:call-template>
          <xsl:call-template name="ddn-head"><xsl:with-param name="t" select="'OBJECT'"/></xsl:call-template>
          <xsl:call-template name="ddn-head"><xsl:with-param name="t" select="'TITLE'"/></xsl:call-template>
          <xsl:call-template name="ddn-head"><xsl:with-param name="t" select="'ISSUE'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:for-each select="dmRef|pmRef|infoEntityRef|dispatchFileName">
          <fo:table-row>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block text-align="center"><xsl:value-of select="position()"/></fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block font-size="{$fs-tiny}pt">
                <xsl:choose>
                  <xsl:when test="self::dmRef">
                    <xsl:call-template name="dm-code-string">
                      <xsl:with-param name="c" select="dmRefIdent/dmCode"/>
                    </xsl:call-template>
                  </xsl:when>
                  <xsl:when test="self::pmRef">
                    <xsl:call-template name="pm-code-string">
                      <xsl:with-param name="c" select="pmRefIdent/pmCode"/>
                    </xsl:call-template>
                  </xsl:when>
                  <xsl:when test="self::infoEntityRef">
                    <xsl:value-of select="@infoEntityRefIdent"/>
                  </xsl:when>
                  <xsl:otherwise><xsl:value-of select="."/></xsl:otherwise>
                </xsl:choose>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block>
                <xsl:value-of select="dmRefAddressItems/dmTitle/techName"/>
                <xsl:if test="dmRefAddressItems/dmTitle/infoName">
                  <xsl:text> — </xsl:text>
                  <xsl:value-of select="dmRefAddressItems/dmTitle/infoName"/>
                </xsl:if>
                <xsl:value-of select="pmRefAddressItems/pmTitle"/>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block text-align="center">
                <xsl:value-of select="dmRefIdent/issueInfo/@issueNumber|pmRefIdent/issueInfo/@issueNumber"/>
                <xsl:if test="dmRefIdent/issueInfo/@inWork|pmRefIdent/issueInfo/@inWork">
                  <xsl:text>-</xsl:text>
                  <xsl:value-of select="dmRefIdent/issueInfo/@inWork|pmRefIdent/issueInfo/@inWork"/>
                </xsl:if>
              </fo:block>
            </fo:table-cell>
          </fo:table-row>
        </xsl:for-each>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template name="ddn-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template name="authorization">
    <xsl:if test="/ddn/identAndStatusSection/ddnStatus/authorization">
      <fo:block space-before="8mm">
        <fo:block font-weight="bold" space-after="1mm">Authorization</fo:block>
        <fo:block space-after="10mm">
          <xsl:value-of select="/ddn/identAndStatusSection/ddnStatus/authorization"/>
        </fo:block>
        <fo:block border-top="{$cell-rule}" width="70mm">
          <fo:block font-size="{$fs-tiny}pt" space-before="1mm">Signature and date</fo:block>
        </fo:block>
      </fo:block>
    </xsl:if>
  </xsl:template>

</xsl:stylesheet>

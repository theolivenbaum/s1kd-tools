<?xml version="1.0" encoding="UTF-8"?>
<!--
  pm.xsl — publication module (pm.xsd).

  A publication module is the assembly order of a publication: nested entries
  that resolve to data modules, other publication modules or external
  publications. It is printed as the publication's contents list — entry titles
  stepped in by nesting level, each referenced object's code set on the right
  behind a dot leader.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="content[pmEntry]">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Contents'"/>
    </xsl:call-template>
    <xsl:apply-templates select="pmEntry"/>
  </xsl:template>

  <xsl:template match="pmEntry">
    <xsl:variable name="depth" select="count(ancestor::pmEntry)"/>

    <xsl:if test="pmEntryTitle">
      <fo:block space-before="{2.5 - $depth * 0.5}mm" space-after="1mm"
                start-indent="{$depth * 6}mm"
                keep-with-next.within-page="always">
        <xsl:attribute name="font-weight">bold</xsl:attribute>
        <xsl:if test="$depth = 0">
          <xsl:attribute name="font-size"><xsl:value-of select="$fs + 1"/>pt</xsl:attribute>
          <xsl:attribute name="space-before">4mm</xsl:attribute>
        </xsl:if>
        <xsl:number level="multiple" count="pmEntry" format="1.1.1.1"/>
        <xsl:text>  </xsl:text>
        <xsl:value-of select="pmEntryTitle"/>
      </fo:block>
    </xsl:if>

    <xsl:apply-templates select="dmRef|pmRef|externalPubRef" mode="toc">
      <xsl:with-param name="depth" select="$depth"/>
    </xsl:apply-templates>

    <xsl:apply-templates select="pmEntry"/>
  </xsl:template>

  <xsl:template match="dmRef|pmRef|externalPubRef" mode="toc">
    <xsl:param name="depth" select="0"/>
    <fo:block start-indent="{($depth + 1) * 6}mm" space-after="0.8mm"
              text-align-last="justify" font-size="{$fs-small}pt">
      <xsl:choose>
        <xsl:when test="dmRefAddressItems/dmTitle">
          <xsl:value-of select="dmRefAddressItems/dmTitle/techName"/>
          <xsl:if test="dmRefAddressItems/dmTitle/infoName">
            <xsl:text> — </xsl:text>
            <xsl:value-of select="dmRefAddressItems/dmTitle/infoName"/>
          </xsl:if>
        </xsl:when>
        <xsl:when test="pmRefAddressItems/pmTitle">
          <xsl:value-of select="pmRefAddressItems/pmTitle"/>
        </xsl:when>
        <xsl:when test="externalPubRefAddressItems/externalPubTitle">
          <xsl:value-of select="externalPubRefAddressItems/externalPubTitle"/>
        </xsl:when>
        <xsl:otherwise>(untitled)</xsl:otherwise>
      </xsl:choose>
      <fo:leader leader-pattern="dots" leader-length.minimum="6mm"
                 leader-length.optimum="30mm" leader-length.maximum="100%"/>
      <fo:inline font-size="{$fs-tiny}pt">
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
          <xsl:otherwise>
            <xsl:value-of select="externalPubRefIdent/externalPubCode"/>
          </xsl:otherwise>
        </xsl:choose>
      </fo:inline>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

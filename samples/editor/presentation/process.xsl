<?xml version="1.0" encoding="UTF-8"?>
<!--
  process.xsl — process data module (process.xsd).

  A process data module drives an interactive session: a sequence of nodes, each
  showing content and branching on what the user answers. On paper the branching
  has to be made explicit, so every node is printed as a numbered block with its
  outcomes listed as "if … go to …".
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="process">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="dmSeq">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Process sequence'"/>
    </xsl:call-template>
    <xsl:apply-templates select="dmNode"/>
  </xsl:template>

  <xsl:template match="dmNode">
    <fo:block space-before="3.5mm" keep-together.within-page="always">
      <xsl:if test="@id">
        <xsl:attribute name="id"><xsl:value-of select="@id"/></xsl:attribute>
      </xsl:if>
      <fo:block font-weight="bold" background-color="{$shade}" border="{$cell-rule}"
                padding="1.2mm" space-after="1.5mm">
        <xsl:text>NODE </xsl:text>
        <xsl:number level="multiple" count="dmNode" format="1.1"/>
        <xsl:if test="@nodeIdent">
          <xsl:text> — </xsl:text>
          <xsl:value-of select="@nodeIdent"/>
        </xsl:if>
      </fo:block>
      <fo:block start-indent="6mm">
        <xsl:apply-templates select="*[not(self::dmNodeAlts|self::dmNode)]"/>
      </fo:block>
      <xsl:apply-templates select="dmNodeAlts"/>
      <xsl:apply-templates select="dmNode"/>
    </fo:block>
  </xsl:template>

  <xsl:template match="dmNodeAlts">
    <fo:table table-layout="fixed" width="{$body-w - 6}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" start-indent="6mm" space-before="1.5mm">
      <fo:table-column column-width="{($body-w - 6) * 0.35}mm"/>
      <fo:table-column column-width="{($body-w - 6) * 0.65}mm"/>
      <fo:table-header>
        <fo:table-row>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold">OUTCOME</fo:block>
          </fo:table-cell>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold">NEXT</fo:block>
          </fo:table-cell>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:for-each select="dmNodeAlt">
          <fo:table-row>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block>
                <xsl:choose>
                  <xsl:when test="@altValue"><xsl:value-of select="@altValue"/></xsl:when>
                  <xsl:otherwise><xsl:value-of select="normalize-space(.)"/></xsl:otherwise>
                </xsl:choose>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm">
              <fo:block>
                <xsl:choose>
                  <xsl:when test="@nextActionRefId">
                    <xsl:text>Go to node </xsl:text>
                    <xsl:for-each select="//*[@id = current()/@nextActionRefId]">
                      <xsl:number level="multiple" count="dmNode" format="1.1"/>
                    </xsl:for-each>
                  </xsl:when>
                  <xsl:otherwise>End of process</xsl:otherwise>
                </xsl:choose>
              </fo:block>
            </fo:table-cell>
          </fo:table-row>
        </xsl:for-each>
      </fo:table-body>
    </fo:table>
  </xsl:template>

</xsl:stylesheet>

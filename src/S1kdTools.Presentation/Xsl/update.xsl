<?xml version="1.0" encoding="UTF-8"?>
<!--
  update.xsl — data update file (update.xsd).

  A data update file records the changes to apply to a CSDB object: each update
  names the object it targets, the operation and the new value. Printed as an
  instruction list, one numbered block per update, so the change can be reviewed
  before it is applied.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="content[update]">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Update instructions'"/>
    </xsl:call-template>
    <xsl:apply-templates select="update"/>
  </xsl:template>

  <xsl:template match="update">
    <fo:block space-before="3mm" keep-together.within-page="always">
      <fo:block font-weight="bold" background-color="{$shade}" border="{$cell-rule}"
                padding="1.2mm" space-after="1.5mm">
        <xsl:text>UPDATE </xsl:text>
        <xsl:number count="update" level="any" format="1"/>
        <xsl:if test="@updateType">
          <xsl:text> — </xsl:text>
          <xsl:value-of select="@updateType"/>
        </xsl:if>
      </fo:block>
      <fo:block start-indent="4mm">
        <xsl:call-template name="attribute-table"/>
        <xsl:apply-templates/>
      </fo:block>
    </fo:block>
  </xsl:template>

  <xsl:template match="updateInstructions|updateInstruction">
    <fo:block space-after="1.5mm"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <!-- The XPath an update targets is data, not prose: set it monospaced, one
       location step per line so a long path stays inside the page. -->
  <xsl:template match="objectPath|updatePath|xpath">
    <fo:block font-family="{$mono-font-family}" font-size="{$fs-small}pt" space-after="1mm">
      <xsl:call-template name="path-lines">
        <xsl:with-param name="path" select="."/>
      </xsl:call-template>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

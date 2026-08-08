<?xml version="1.0" encoding="UTF-8"?>
<!--
  descript.xsl — descriptive data module (descript.xsd).

  A descriptive data module is prose: levelled paragraphs, figures and tables.
  Almost all of that is common construct handling, so this stylesheet only has
  to open the description and give the top-level paragraphs their running
  header markers.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="description">
    <xsl:apply-templates/>
  </xsl:template>

  <!-- A description may open with unlevelled paragraphs before the first
       levelled section; those are handled by the common para template. -->
  <xsl:template match="description/title">
    <fo:block font-size="{$fs + 1}pt" font-weight="bold" space-before="4mm" space-after="2mm"
              keep-with-next.within-page="always">
      <fo:marker marker-class-name="s1kd-section"><xsl:value-of select="."/></fo:marker>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

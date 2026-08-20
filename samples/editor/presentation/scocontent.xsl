<?xml version="1.0" encoding="UTF-8"?>
<!--
  scocontent.xsl — SCO content data module (scocontent.xsd).

  The content of one sharable content object in a SCORM package. On screen it is
  a lesson page; on paper it is the same prose with its learning events called
  out, which is what an instructor works from.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="scoContent">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="scoEntry">
    <fo:block space-before="3mm">
      <xsl:if test="scoEntryTitle|title">
        <fo:block font-weight="bold" font-size="{$fs + 1}pt" space-after="1.5mm"
                  keep-with-next.within-page="always">
          <fo:marker marker-class-name="s1kd-section">
            <xsl:value-of select="scoEntryTitle|title"/>
          </fo:marker>
          <xsl:number level="multiple" count="scoEntry" format="1.1.1"/>
          <xsl:text>  </xsl:text>
          <xsl:value-of select="scoEntryTitle|title"/>
        </fo:block>
      </xsl:if>
      <fo:block start-indent="{count(ancestor-or-self::scoEntry) * 3}mm">
        <xsl:apply-templates select="*[not(self::scoEntryTitle|self::title)]"/>
      </fo:block>
    </fo:block>
  </xsl:template>

  <!-- A learning event is the interactive part of the lesson; on paper it is
       set off in a frame so an instructor can see where the screen changes. -->
  <xsl:template match="learningEvent|interactiveEvent">
    <fo:block border="{$cell-rule}" padding="1.5mm" space-before="2.5mm" space-after="2.5mm">
      <fo:block font-weight="bold" font-size="{$fs-small}pt" space-after="1mm">
        <xsl:text>LEARNING EVENT</xsl:text>
        <xsl:if test="@learnEventCode">
          <xsl:text> — </xsl:text><xsl:value-of select="@learnEventCode"/>
        </xsl:if>
      </fo:block>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

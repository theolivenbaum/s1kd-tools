<?xml version="1.0" encoding="UTF-8"?>
<!--
  scormcontentpackage.xsl — SCORM content package (scormcontentpackage.xsd).

  A SCORM content package is the assembly order of a course. Printed, it is the
  course outline: nested entries with the sharable content objects they resolve
  to set behind a dot leader.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="content[scoEntry]">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Course outline'"/>
    </xsl:call-template>
    <xsl:apply-templates select="scoEntry"/>
  </xsl:template>

  <xsl:template match="scoEntry">
    <xsl:variable name="depth" select="count(ancestor::scoEntry)"/>
    <xsl:if test="scoEntryTitle">
      <fo:block start-indent="{$depth * 6}mm" space-before="2.5mm" space-after="1mm"
                font-weight="bold" keep-with-next.within-page="always">
        <xsl:if test="$depth = 0">
          <xsl:attribute name="font-size"><xsl:value-of select="$fs + 1"/>pt</xsl:attribute>
        </xsl:if>
        <xsl:number level="multiple" count="scoEntry" format="1.1.1"/>
        <xsl:text>  </xsl:text>
        <xsl:value-of select="scoEntryTitle"/>
      </fo:block>
    </xsl:if>

    <xsl:for-each select="dmRef">
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
          <xsl:otherwise>(untitled)</xsl:otherwise>
        </xsl:choose>
        <fo:leader leader-pattern="dots" leader-length.minimum="6mm"
                   leader-length.optimum="25mm" leader-length.maximum="100%"/>
        <fo:inline font-size="{$fs-tiny}pt">
          <xsl:call-template name="dm-code-string">
            <xsl:with-param name="c" select="dmRefIdent/dmCode"/>
          </xsl:call-template>
        </fo:inline>
      </fo:block>
    </xsl:for-each>

    <xsl:apply-templates select="scoEntry"/>
  </xsl:template>

</xsl:stylesheet>

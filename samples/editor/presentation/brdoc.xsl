<?xml version="1.0" encoding="UTF-8"?>
<!--
  brdoc.xsl — business rules document data module (brdoc.xsd).

  The human-readable companion of a BREX data module: the decisions a project
  has taken, written out. Prose is presented as in a descriptive data module;
  each recorded decision gets a boxed identifier so it can be cited.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="brDoc">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="brLevelledPara">
    <fo:block space-before="3mm">
      <xsl:if test="title">
        <fo:block font-weight="bold" font-size="{$fs + 1}pt" space-after="1.5mm"
                  keep-with-next.within-page="always">
          <fo:marker marker-class-name="s1kd-section"><xsl:value-of select="title"/></fo:marker>
          <xsl:number level="multiple" count="brLevelledPara" format="1.1.1"/>
          <xsl:text>  </xsl:text>
          <xsl:apply-templates select="title" mode="inline"/>
        </fo:block>
      </xsl:if>
      <fo:block start-indent="{count(ancestor-or-self::brLevelledPara) * 3}mm">
        <xsl:apply-templates select="*[not(self::title)]"/>
      </fo:block>
    </fo:block>
  </xsl:template>

  <xsl:template match="brDecision">
    <fo:block border="{$cell-rule}" padding="1.5mm" space-before="2.5mm" space-after="2.5mm"
              start-indent="4mm">
      <fo:block font-weight="bold" font-size="{$fs-small}pt" space-after="1mm">
        <xsl:text>DECISION </xsl:text>
        <xsl:value-of select="@brDecisionIdentNumber|brDecisionIdent"/>
      </fo:block>
      <xsl:apply-templates select="*[not(self::brDecisionIdent)]"/>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

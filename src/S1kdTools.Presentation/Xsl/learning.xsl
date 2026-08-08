<?xml version="1.0" encoding="UTF-8"?>
<!--
  learning.xsl — training data module (learning.xsd).

  A training data module holds the four parts of a lesson — plan, overview,
  content and assessment. They are printed in that order under named headings,
  with assessment questions numbered and their options lettered.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="learning">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="learningPlan|learningOverview|learningContent|learningSummary|learningAssessment">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="number"><xsl:number count="learningPlan|learningOverview|learningContent|learningSummary|learningAssessment" level="any" format="1."/></xsl:with-param>
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="title"><xsl:value-of select="title"/></xsl:when>
          <xsl:otherwise>
            <xsl:call-template name="camel-to-words">
              <xsl:with-param name="text" select="local-name()"/>
            </xsl:call-template>
          </xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
    <fo:block start-indent="6mm">
      <xsl:apply-templates select="*[not(self::title)]"/>
    </fo:block>
  </xsl:template>

  <xsl:template match="learningObjective">
    <fo:block space-after="1.5mm">
      <fo:inline font-weight="bold">
        <xsl:text>Objective </xsl:text>
        <xsl:number count="learningObjective" level="any" format="1"/>
        <xsl:text>: </xsl:text>
      </fo:inline>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="interactivity|question|assessment">
    <fo:block space-before="2.5mm" keep-together.within-page="always">
      <fo:block font-weight="bold" space-after="1mm">
        <xsl:text>Question </xsl:text>
        <xsl:number count="interactivity|question|assessment" level="any" format="1"/>
      </fo:block>
      <fo:block start-indent="4mm"><xsl:apply-templates/></fo:block>
    </fo:block>
  </xsl:template>

  <xsl:template match="option|answer">
    <fo:block start-indent="8mm" space-after="0.8mm">
      <fo:inline font-weight="bold">
        <xsl:number count="option|answer" format="A"/>
        <xsl:text>. </xsl:text>
      </fo:inline>
      <xsl:apply-templates/>
      <xsl:if test="@correct = 'true' or @isCorrect = 'true'">
        <fo:inline font-weight="bold"> ✓</fo:inline>
      </xsl:if>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

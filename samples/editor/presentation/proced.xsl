<?xml version="1.0" encoding="UTF-8"?>
<!--
  proced.xsl — procedural data module (proced.xsd).

  Laid out the way a civil aircraft maintenance task is laid out on paper:

    1  Reason for the job          (commonInfo)
    2  Job set-up information      (preliminaryRqmts: conditions, tools,
                                    consumables, expendables, safety)
    3  Procedure                   (mainProcedure)
    4  Close-up                    (closeRqmts)

  Steps are numbered 1. / A. / (1) / (a) down the hierarchy by the shared
  step-number template.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="procedure">
    <xsl:if test="commonInfo">
      <xsl:call-template name="section-heading">
        <xsl:with-param name="number" select="'1.'"/>
        <xsl:with-param name="text" select="'Reason for the job'"/>
      </xsl:call-template>
      <fo:block start-indent="6mm">
        <xsl:apply-templates select="commonInfo/*"/>
      </fo:block>
    </xsl:if>

    <xsl:if test="preliminaryRqmts">
      <xsl:call-template name="preliminary-requirements">
        <xsl:with-param name="node" select="preliminaryRqmts"/>
        <xsl:with-param name="number">
          <xsl:choose>
            <xsl:when test="commonInfo">2.</xsl:when>
            <xsl:otherwise>1.</xsl:otherwise>
          </xsl:choose>
        </xsl:with-param>
        <xsl:with-param name="heading" select="'Job set-up information'"/>
      </xsl:call-template>
    </xsl:if>

    <xsl:call-template name="section-heading">
      <xsl:with-param name="number">
        <xsl:choose>
          <xsl:when test="commonInfo">3.</xsl:when>
          <xsl:otherwise>2.</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
      <xsl:with-param name="text" select="'Procedure'"/>
    </xsl:call-template>
    <xsl:apply-templates select="mainProcedure/*"/>

    <xsl:if test="closeRqmts and not(closeRqmts/reqCondGroup/noConds and count(closeRqmts/*) = 1)">
      <xsl:call-template name="section-heading">
        <xsl:with-param name="number">
          <xsl:choose>
            <xsl:when test="commonInfo">4.</xsl:when>
            <xsl:otherwise>3.</xsl:otherwise>
          </xsl:choose>
        </xsl:with-param>
        <xsl:with-param name="text" select="'Close-up'"/>
      </xsl:call-template>
      <xsl:apply-templates select="closeRqmts/*"/>
    </xsl:if>
  </xsl:template>

  <!-- Close-up conditions are plain requirements, not a job set-up table. -->
  <xsl:template match="closeRqmts/reqCondGroup">
    <xsl:apply-templates select="reqCondNoRef|reqCondDm|reqCondPm"/>
  </xsl:template>

  <xsl:template match="reqCondNoRef|reqCondDm|reqCondPm">
    <fo:block start-indent="6mm" space-after="1.5mm">
      <xsl:value-of select="reqCond"/>
      <xsl:if test="dmRef">
        <xsl:text> </xsl:text>
        <xsl:apply-templates select="dmRef"/>
      </xsl:if>
    </fo:block>
  </xsl:template>

  <xsl:template match="closeRqmts/reqCondGroup/noConds">
    <fo:block start-indent="6mm" font-style="italic">No close-up conditions</fo:block>
  </xsl:template>

</xsl:stylesheet>

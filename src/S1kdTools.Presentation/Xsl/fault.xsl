<?xml version="1.0" encoding="UTF-8"?>
<!--
  fault.xsl — fault isolation data module (fault.xsd).

  Fault isolation is a decision tree: each isolation step asks a question and
  each answer routes the technician to another step or ends the procedure. The
  printed form of that is a question block per step with its answers in a
  two-column ANSWER / ACTION table, which is what this stylesheet produces.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="faultIsolation">
    <xsl:if test="faultDescr|faultReports">
      <xsl:call-template name="section-heading">
        <xsl:with-param name="number" select="'1.'"/>
        <xsl:with-param name="text" select="'Fault description'"/>
      </xsl:call-template>
      <fo:block start-indent="6mm">
        <xsl:apply-templates select="faultDescr/*|faultReports/*"/>
      </fo:block>
    </xsl:if>

    <xsl:apply-templates select="faultIsolationProcedure"/>
    <xsl:apply-templates select="correlatedFaultInfo"/>
  </xsl:template>

  <xsl:template match="faultIsolationProcedure">
    <xsl:apply-templates select="isolationProcedure"/>
  </xsl:template>

  <xsl:template match="isolationProcedure">
    <xsl:variable name="offset" select="count(../../faultDescr|../../faultReports)"/>

    <xsl:if test="preliminaryRqmts">
      <xsl:call-template name="preliminary-requirements">
        <xsl:with-param name="node" select="preliminaryRqmts"/>
        <xsl:with-param name="number" select="concat($offset + 1, '.')"/>
        <xsl:with-param name="heading" select="'Job set-up information'"/>
      </xsl:call-template>
    </xsl:if>

    <xsl:call-template name="section-heading">
      <xsl:with-param name="number" select="concat($offset + 2, '.')"/>
      <xsl:with-param name="text" select="'Fault isolation procedure'"/>
    </xsl:call-template>
    <xsl:apply-templates select="isolationMainProcedure/*"/>

    <xsl:if test="closeRqmts/reqCondGroup/reqCondNoRef|closeRqmts/reqCondGroup/reqCondDm">
      <xsl:call-template name="section-heading">
        <xsl:with-param name="number" select="concat($offset + 3, '.')"/>
        <xsl:with-param name="text" select="'Close-up'"/>
      </xsl:call-template>
      <fo:block start-indent="6mm">
        <xsl:for-each select="closeRqmts/reqCondGroup/reqCondNoRef|closeRqmts/reqCondGroup/reqCondDm">
          <fo:block space-after="1.5mm">
            <xsl:value-of select="reqCond"/>
            <xsl:if test="dmRef"><xsl:text> </xsl:text><xsl:apply-templates select="dmRef"/></xsl:if>
          </fo:block>
        </xsl:for-each>
      </fo:block>
    </xsl:if>
  </xsl:template>

  <xsl:template match="isolationStep">
    <fo:block space-before="4mm" keep-together.within-page="always">
      <xsl:if test="@id">
        <xsl:attribute name="id"><xsl:value-of select="@id"/></xsl:attribute>
      </xsl:if>
      <xsl:call-template name="change-attributes"/>
      <xsl:call-template name="applicability-annotation"/>

      <fo:block font-weight="bold" background-color="{$shade}" padding="1.2mm"
                border="{$cell-rule}" space-after="1.5mm">
        <xsl:text>STEP </xsl:text>
        <xsl:call-template name="isolation-step-label"/>
      </fo:block>

      <fo:block start-indent="6mm">
        <xsl:apply-templates select="isolationStepQuestion/*|preliminaryRqmts|para|warning|caution|note|figure|table"/>
      </fo:block>

      <xsl:if test="isolationStepAnswer">
        <fo:table table-layout="fixed" width="{$body-w - 6}mm" border-collapse="collapse"
                  font-size="{$fs-small}pt" start-indent="6mm" space-before="2mm">
          <fo:table-column column-width="{($body-w - 6) * 0.32}mm"/>
          <fo:table-column column-width="{($body-w - 6) * 0.68}mm"/>
          <fo:table-header>
            <fo:table-row>
              <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
                <fo:block font-weight="bold">ANSWER</fo:block>
              </fo:table-cell>
              <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
                <fo:block font-weight="bold">ACTION</fo:block>
              </fo:table-cell>
            </fo:table-row>
          </fo:table-header>
          <fo:table-body>
            <xsl:apply-templates select="isolationStepAnswer" mode="answer"/>
          </fo:table-body>
        </fo:table>
      </xsl:if>

      <xsl:apply-templates select="isolationStep"/>
    </fo:block>
  </xsl:template>

  <xsl:template match="isolationStepAnswer" mode="answer">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block font-weight="bold">
          <xsl:choose>
            <xsl:when test="isolationStepAnswerValue">
              <xsl:value-of select="isolationStepAnswerValue"/>
            </xsl:when>
            <xsl:otherwise>
              <xsl:value-of select="normalize-space(substring-before(concat(., '.'), '.'))"/>
            </xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:apply-templates select="*[not(self::isolationStepAnswerValue)]"/>
          <xsl:if test="@nextActionRefId">
            <fo:block font-weight="bold" space-before="0.8mm">
              <xsl:text>Go to </xsl:text>
              <xsl:variable name="next" select="//*[@id = current()/@nextActionRefId]"/>
              <xsl:choose>
                <xsl:when test="$next/self::isolationProcedureEnd">the end of the procedure</xsl:when>
                <xsl:otherwise>
                  <xsl:text>step </xsl:text>
                  <xsl:for-each select="$next">
                    <xsl:call-template name="isolation-step-label"/>
                  </xsl:for-each>
                </xsl:otherwise>
              </xsl:choose>
            </fo:block>
          </xsl:if>
        </fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <xsl:template match="isolationStepQuestion">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="correctiveAction">
    <fo:block space-before="1.5mm">
      <fo:inline font-weight="bold">Corrective action: </fo:inline>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="isolationProcedureEnd">
    <fo:block space-before="4mm" font-weight="bold" border="{$cell-rule}" padding="1.5mm"
              background-color="{$shade}">
      <xsl:if test="@id">
        <xsl:attribute name="id"><xsl:value-of select="@id"/></xsl:attribute>
      </xsl:if>
      <xsl:text>END OF FAULT ISOLATION PROCEDURE</xsl:text>
    </fo:block>
    <xsl:if test="*">
      <fo:block start-indent="6mm" space-before="1.5mm"><xsl:apply-templates/></fo:block>
    </xsl:if>
  </xsl:template>

  <xsl:template match="correlatedFaultInfo">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Correlated fault information'"/>
    </xsl:call-template>
    <fo:block start-indent="6mm"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <!-- 1 / 1.1 / 1.1.1 through the isolation step hierarchy. -->
  <xsl:template name="isolation-step-label">
    <xsl:number level="multiple" count="isolationStep" format="1.1.1"/>
  </xsl:template>

</xsl:stylesheet>

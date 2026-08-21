<?xml version="1.0" encoding="UTF-8"?>
<!--
  sb.xsl — service bulletin data module (sb.xsd).

  Service bulletins are read in a fixed order — management data, planning
  information, material information, accomplishment instructions, additional
  information — with the planning and material sections broken into lettered
  paragraphs (A. Effectivity, B. Reason, C. Description, …). The section
  headings here are derived from the element names of the schema, so a bulletin
  that carries more of the model than the sample prints its extra paragraphs in
  the right place without a stylesheet change.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="sb">
    <xsl:apply-templates select="sbManagementInfo"/>
    <xsl:apply-templates select="sbRevisionInfo"/>
    <xsl:apply-templates select="sbSummary"/>

    <xsl:call-template name="sb-section">
      <xsl:with-param name="node" select="sbPlanningInfo"/>
      <xsl:with-param name="number" select="'1.'"/>
      <xsl:with-param name="title" select="'PLANNING INFORMATION'"/>
    </xsl:call-template>

    <xsl:call-template name="sb-section">
      <xsl:with-param name="node" select="sbMaterialInfo"/>
      <xsl:with-param name="number" select="'2.'"/>
      <xsl:with-param name="title" select="'MATERIAL INFORMATION'"/>
    </xsl:call-template>

    <xsl:call-template name="sb-section">
      <xsl:with-param name="node" select="sbAccomplishmentInstructions"/>
      <xsl:with-param name="number" select="'3.'"/>
      <xsl:with-param name="title" select="'ACCOMPLISHMENT INSTRUCTIONS'"/>
    </xsl:call-template>

    <xsl:call-template name="sb-section">
      <xsl:with-param name="node" select="sbAdditionalInfo"/>
      <xsl:with-param name="number" select="'4.'"/>
      <xsl:with-param name="title" select="'ADDITIONAL INFORMATION'"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="sbManagementInfo">
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" space-after="3mm">
      <fo:table-column column-width="{$body-w * 0.32}mm"/>
      <fo:table-column column-width="{$body-w * 0.68}mm"/>
      <fo:table-body>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Service bulletin number'"/>
          <xsl:with-param name="value" select="sbNumber"/>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'ATA chapter'"/>
          <xsl:with-param name="value" select="sbAta"/>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Compliance category'"/>
          <xsl:with-param name="value" select="sbCompliance/@sbComplianceCategory"/>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Task category'"/>
          <xsl:with-param name="value" select="sbTaskType/@sbTaskCategory"/>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Original issue date'"/>
          <xsl:with-param name="value">
            <xsl:call-template name="format-date">
              <xsl:with-param name="date" select="sbOriginalIssueDate/issueDate"/>
            </xsl:call-template>
          </xsl:with-param>
        </xsl:call-template>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template match="sbRevisionInfo|sbSummary">
    <xsl:if test="not(noInfo)">
      <xsl:call-template name="section-heading">
        <xsl:with-param name="text">
          <xsl:choose>
            <xsl:when test="self::sbRevisionInfo">REVISION INFORMATION</xsl:when>
            <xsl:otherwise>SUMMARY</xsl:otherwise>
          </xsl:choose>
        </xsl:with-param>
      </xsl:call-template>
      <fo:block start-indent="6mm"><xsl:apply-templates/></fo:block>
    </xsl:if>
  </xsl:template>

  <xsl:template name="sb-section">
    <xsl:param name="node"/>
    <xsl:param name="number"/>
    <xsl:param name="title"/>
    <xsl:if test="$node and not($node/noInfo)">
      <xsl:call-template name="section-heading">
        <xsl:with-param name="number" select="$number"/>
        <xsl:with-param name="text" select="$title"/>
      </xsl:call-template>
      <xsl:apply-templates select="$node/*" mode="sb-paragraph"/>
    </xsl:if>
  </xsl:template>

  <!--
    One lettered paragraph per child element, titled from the element name with
    the schema's "sb" prefix removed: sbEffectivity -> "Effectivity".
  -->
  <xsl:template match="*" mode="sb-paragraph">
    <xsl:call-template name="subsection-heading">
      <xsl:with-param name="number">
        <xsl:number format="A." count="*"/>
      </xsl:with-param>
      <xsl:with-param name="text">
        <xsl:call-template name="sb-paragraph-title"/>
      </xsl:with-param>
    </xsl:call-template>
    <fo:block start-indent="6mm">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template name="sb-paragraph-title">
    <xsl:variable name="n" select="local-name()"/>
    <xsl:call-template name="camel-to-words">
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="starts-with($n, 'sb')"><xsl:value-of select="substring($n, 3)"/></xsl:when>
          <xsl:otherwise><xsl:value-of select="$n"/></xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <!-- Effectivity is a list of aircraft; print it as a table, not as prose. -->
  <xsl:template match="sbEffectivity" mode="sb-paragraph">
    <xsl:call-template name="subsection-heading">
      <xsl:with-param name="number"><xsl:number format="A." count="*"/></xsl:with-param>
      <xsl:with-param name="text" select="'Effectivity'"/>
    </xsl:call-template>
    <fo:block start-indent="6mm">
      <xsl:apply-templates select="*[not(self::sbAircraftEffectivity)]"/>
      <xsl:if test="sbAircraftEffectivity">
        <fo:table table-layout="fixed" width="{$body-w - 6}mm" border-collapse="collapse"
                  font-size="{$fs-small}pt" space-before="1.5mm">
          <fo:table-column column-width="{($body-w - 6) * 0.3}mm"/>
          <fo:table-column column-width="{($body-w - 6) * 0.7}mm"/>
          <fo:table-header>
            <fo:table-row>
              <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
                <fo:block font-weight="bold">MODEL</fo:block>
              </fo:table-cell>
              <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
                <fo:block font-weight="bold">SERIAL NUMBERS</fo:block>
              </fo:table-cell>
            </fo:table-row>
          </fo:table-header>
          <fo:table-body>
            <xsl:for-each select="sbAircraftEffectivity">
              <fo:table-row>
                <fo:table-cell border="{$cell-rule}" padding="1.2mm">
                  <fo:block><xsl:value-of select="sbAircraftModel|@sbAircraftModel"/></fo:block>
                </fo:table-cell>
                <fo:table-cell border="{$cell-rule}" padding="1.2mm">
                  <fo:block><xsl:value-of select="normalize-space(sbSerialNumbers|.)"/></fo:block>
                </fo:table-cell>
              </fo:table-row>
            </xsl:for-each>
          </fo:table-body>
        </fo:table>
      </xsl:if>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

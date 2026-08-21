<?xml version="1.0" encoding="UTF-8"?>
<!--
  schedul.xsl — maintenance planning data module (schedul.xsd).

  A maintenance planning data module answers "what has to be done, to what, and
  how often". It is printed as the scheduled-maintenance tables of a planning
  document: one row per task with its threshold, repeat interval, zone and
  estimated man-hours.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="maintPlanning">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="inspectionDefinition|taskDefinition">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="title"><xsl:value-of select="title"/></xsl:when>
          <xsl:when test="self::inspectionDefinition">Inspections</xsl:when>
          <xsl:otherwise>Tasks</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <fo:table-column column-width="{$body-w * 0.16}mm"/>
      <fo:table-column column-width="{$body-w * 0.38}mm"/>
      <fo:table-column column-width="{$body-w * 0.16}mm"/>
      <fo:table-column column-width="{$body-w * 0.16}mm"/>
      <fo:table-column column-width="{$body-w * 0.14}mm"/>
      <fo:table-header>
        <fo:table-row>
          <xsl:call-template name="sched-head"><xsl:with-param name="t" select="'TASK No.'"/></xsl:call-template>
          <xsl:call-template name="sched-head"><xsl:with-param name="t" select="'DESCRIPTION'"/></xsl:call-template>
          <xsl:call-template name="sched-head"><xsl:with-param name="t" select="'THRESHOLD'"/></xsl:call-template>
          <xsl:call-template name="sched-head"><xsl:with-param name="t" select="'INTERVAL'"/></xsl:call-template>
          <xsl:call-template name="sched-head"><xsl:with-param name="t" select="'ZONE / MHR'"/></xsl:call-template>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select="inspection|task" mode="sched"/>
      </fo:table-body>
    </fo:table>

    <xsl:apply-templates select="*[not(self::title|self::inspection|self::task)]"/>
  </xsl:template>

  <xsl:template name="sched-head">
    <xsl:param name="t"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-weight="bold" font-size="{$fs-tiny}pt"><xsl:value-of select="$t"/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template match="inspection|task" mode="sched">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:value-of select="@inspectionIdent|@taskIdent|inspectionIdent|taskIdent"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:value-of select="inspectionName|taskName|name|title"/>
        </fo:block>
        <xsl:if test="inspectionDescr|taskDescr|descr">
          <fo:block font-size="{$fs-tiny}pt" color="#444444" space-before="0.5mm">
            <xsl:value-of select="inspectionDescr|taskDescr|descr"/>
          </fo:block>
        </xsl:if>
        <xsl:if test="dmRef">
          <fo:block font-size="{$fs-tiny}pt" space-before="0.5mm">
            <xsl:apply-templates select="dmRef"/>
          </fo:block>
        </xsl:if>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:call-template name="limit-text">
          <xsl:with-param name="node" select="threshold|limit[@limitType='threshold']"/>
        </xsl:call-template></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:call-template name="limit-text">
          <xsl:with-param name="node" select="interval|limit[@limitType='interval']"/>
        </xsl:call-template></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:value-of select="zoneRef/@zoneNumber|zone"/>
          <xsl:if test="manHours">
            <xsl:text> / </xsl:text>
            <xsl:value-of select="manHours"/>
          </xsl:if>
        </fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <!-- "600 FH", "24 MO" — a limit is a value plus a unit of measure. -->
  <xsl:template name="limit-text">
    <xsl:param name="node"/>
    <xsl:choose>
      <xsl:when test="$node/@limitValue">
        <xsl:value-of select="$node/@limitValue"/>
        <xsl:text> </xsl:text>
        <xsl:value-of select="$node/@limitUnitOfMeasure"/>
      </xsl:when>
      <xsl:when test="$node">
        <xsl:value-of select="normalize-space($node)"/>
      </xsl:when>
      <xsl:otherwise>—</xsl:otherwise>
    </xsl:choose>
  </xsl:template>

</xsl:stylesheet>

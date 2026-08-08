<?xml version="1.0" encoding="UTF-8"?>
<!--
  crew.xsl — crew/operator information data module (crew.xsd).

  Crew data is read in the cockpit, so it is printed the way a flight crew
  operating manual prints it: the drill as a two-column challenge/response
  list, reference-card items as a plain checklist, everything set tight.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template match="crew">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="crewRefCard">
    <xsl:if test="title">
      <xsl:call-template name="section-heading">
        <xsl:with-param name="text" select="title"/>
      </xsl:call-template>
    </xsl:if>
    <xsl:apply-templates select="*[not(self::title)]"/>
  </xsl:template>

  <xsl:template match="crewDrill">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="title"><xsl:value-of select="title"/></xsl:when>
          <xsl:otherwise>Drill</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
    <xsl:apply-templates select="*[not(self::title)]"/>
  </xsl:template>

  <!--
    A drill is a challenge/response list: the action on the left, the required
    state on the right, joined by a dot leader as on a printed drill card.
  -->
  <xsl:template match="crewDrillStep[crewDrillAction and crewDrillResponse]">
    <fo:block text-align-last="justify" space-after="1.2mm"
              start-indent="{count(ancestor::crewDrillStep) * 6 + 6}mm">
      <xsl:call-template name="change-attributes"/>
      <xsl:apply-templates select="crewDrillAction/node()"/>
      <fo:leader leader-pattern="dots" leader-length.minimum="6mm"
                 leader-length.optimum="25mm" leader-length.maximum="100%"/>
      <fo:inline font-weight="bold"><xsl:apply-templates select="crewDrillResponse/node()"/></fo:inline>
    </fo:block>
    <xsl:apply-templates select="warning|caution|note|crewDrillStep"/>
  </xsl:template>

  <xsl:template match="crewDrillAction|crewDrillResponse">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template match="crewMemberGroup">
    <xsl:call-template name="subsection-heading">
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="title"><xsl:value-of select="title"/></xsl:when>
          <xsl:otherwise>Crew members</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
    <xsl:apply-templates select="*[not(self::title)]"/>
  </xsl:template>

</xsl:stylesheet>

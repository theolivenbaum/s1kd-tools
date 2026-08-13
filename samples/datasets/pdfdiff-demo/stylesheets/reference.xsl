<?xml version="1.0" encoding="UTF-8"?>
<!--
  reference.xsl — the "other toolchain".

  A deliberately opinionated presentation stylesheet for an S1000D descriptive data
  module: A4 with asymmetric margins, a ruled running header carrying the technical
  name and the data module code, a ruled footer with a folio, numbered section titles
  at three levels, a shaded and bordered warning box, a ruled table with a shaded
  header row, and a hanging-indent list.

  In the s1kd-pdfdiff demonstration this stylesheet stands in for the toolchain whose
  PDF you have but whose stylesheet you do not — the thing being reverse engineered.
  Nothing here is meant as an S1000D house style; it is meant to have enough distinct,
  measurable decisions in it that a comparison against a plainer stylesheet has
  something to find.
-->
<xsl:stylesheet version="1.0"
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:output method="xml" indent="no" encoding="UTF-8"/>

  <xsl:param name="publication" select="'AIRCRAFT MAINTENANCE MANUAL'"/>

  <!-- ============================================================== page geometry -->

  <xsl:template match="/dmodule">
    <fo:root font-family="serif" xml:lang="en">
      <fo:layout-master-set>
        <fo:simple-page-master master-name="body"
          page-width="210mm" page-height="297mm"
          margin-left="25mm" margin-right="18mm"
          margin-top="12mm" margin-bottom="12mm">
          <fo:region-body margin-top="14mm" margin-bottom="13mm"/>
          <fo:region-before extent="12mm"/>
          <fo:region-after extent="11mm"/>
        </fo:simple-page-master>
      </fo:layout-master-set>

      <fo:page-sequence master-reference="body">
        <fo:static-content flow-name="xsl-region-before">
          <fo:block font-size="8pt" letter-spacing="0.3pt"
            border-after-style="solid" border-after-width="0.7pt" padding-after="3pt">
            <xsl:value-of select="$publication"/>
            <fo:leader leader-pattern="space"/>
            <xsl:apply-templates select="identAndStatusSection/dmAddress/dmIdent/dmCode"
              mode="code"/>
          </fo:block>
        </fo:static-content>

        <fo:static-content flow-name="xsl-region-after">
          <fo:block font-size="8pt"
            border-before-style="solid" border-before-width="0.4pt" padding-before="3pt">
            <xsl:call-template name="issue-date"/>
            <fo:leader leader-pattern="space"/>
            <fo:inline>Page </fo:inline>
            <fo:page-number/>
          </fo:block>
        </fo:static-content>

        <fo:flow flow-name="xsl-region-body">
          <xsl:apply-templates select="identAndStatusSection/dmAddress/dmAddressItems/dmTitle"/>
          <xsl:apply-templates select="content/description"/>
        </fo:flow>
      </fo:page-sequence>
    </fo:root>
  </xsl:template>

  <!-- The data module code, assembled from its parts the way a folio prints it. -->
  <xsl:template match="dmCode" mode="code">
    <xsl:text>DMC-</xsl:text>
    <xsl:value-of select="@modelIdentCode"/>-<xsl:value-of select="@systemDiffCode"/>
    <xsl:text>-</xsl:text>
    <xsl:value-of select="@systemCode"/>-<xsl:value-of select="@subSystemCode"/>
    <xsl:value-of select="@subSubSystemCode"/>-<xsl:value-of select="@assyCode"/>
    <xsl:text>-</xsl:text>
    <xsl:value-of select="@disassyCode"/><xsl:value-of select="@disassyCodeVariant"/>
    <xsl:text>-</xsl:text>
    <xsl:value-of select="@infoCode"/><xsl:value-of select="@infoCodeVariant"/>
    <xsl:value-of select="@itemLocationCode"/>
  </xsl:template>

  <xsl:template name="issue-date">
    <xsl:variable name="d" select="/dmodule/identAndStatusSection/dmAddress/dmAddressItems/issueDate"/>
    <xsl:text>Issue </xsl:text>
    <xsl:value-of select="/dmodule/identAndStatusSection/dmAddress/dmIdent/issueInfo/@issueNumber"/>
    <xsl:text> — </xsl:text>
    <xsl:value-of select="$d/@year"/>-<xsl:value-of select="$d/@month"/>-<xsl:value-of select="$d/@day"/>
  </xsl:template>

  <!-- ================================================================== title block -->

  <xsl:template match="dmTitle">
    <fo:block font-size="18pt" font-weight="bold" space-after="4pt"
      border-after-style="solid" border-after-width="1.2pt" padding-after="4pt">
      <xsl:value-of select="techName"/>
    </fo:block>
    <fo:block font-size="12pt" font-style="italic" space-after="14pt">
      <xsl:value-of select="infoName"/>
    </fo:block>
  </xsl:template>

  <!-- ===================================================================== sections -->

  <!--
    Top-level sections start a new page. That is a real editorial decision some
    toolchains make and some do not, and it is the kind of decision a comparison has to
    be able to surface: it changes the page count before it changes anything else.
  -->
  <xsl:template match="description/levelledPara">
    <fo:block space-before="0pt">
      <!-- preceding-sibling, not position(): apply-templates also selects the
           whitespace text nodes between the elements, so position() is not 1 for the
           first section. -->
      <xsl:if test="preceding-sibling::levelledPara">
        <xsl:attribute name="break-before">page</xsl:attribute>
      </xsl:if>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="levelledPara">
    <fo:block space-before="10pt">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="levelledPara/title">
    <fo:block space-after="6pt" keep-with-next.within-page="always">
      <xsl:attribute name="font-size">
        <xsl:choose>
          <xsl:when test="count(ancestor::levelledPara) = 1">14pt</xsl:when>
          <xsl:when test="count(ancestor::levelledPara) = 2">11.5pt</xsl:when>
          <xsl:otherwise>10pt</xsl:otherwise>
        </xsl:choose>
      </xsl:attribute>
      <xsl:attribute name="font-weight">bold</xsl:attribute>
      <xsl:call-template name="section-number"/>
      <xsl:value-of select="."/>
    </fo:block>
  </xsl:template>

  <!-- 1, 1.1, 1.1.1 — counted over levelledPara ancestors. -->
  <xsl:template name="section-number">
    <xsl:for-each select="ancestor::levelledPara">
      <xsl:number count="levelledPara" level="single"/>
      <xsl:text>.</xsl:text>
    </xsl:for-each>
    <xsl:text>  </xsl:text>
  </xsl:template>

  <!-- ======================================================================== text -->

  <xsl:template match="para">
    <fo:block font-size="10pt" line-height="12.5pt" text-align="justify"
      space-after="6pt" start-indent="6mm">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="emphasis">
    <fo:inline font-weight="bold"><xsl:apply-templates/></fo:inline>
  </xsl:template>

  <!-- ======================================================== warnings and cautions -->

  <xsl:template match="warning | caution">
    <fo:block space-before="8pt" space-after="8pt" start-indent="6mm"
      background-color="#e8e8e8"
      border-style="solid" border-width="0.8pt" padding="5pt">
      <fo:block font-size="9pt" font-weight="bold" letter-spacing="0.6pt" space-after="3pt">
        <xsl:value-of
          select="translate(local-name(), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ')"/>
      </fo:block>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="warningAndCautionPara">
    <fo:block font-size="9pt" line-height="11pt" text-align="justify">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="note">
    <fo:block space-before="6pt" space-after="6pt" start-indent="12mm"
      border-start-style="solid" border-start-width="2pt" padding-start="4pt">
      <fo:block font-size="9pt" font-weight="bold">NOTE</fo:block>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="notePara">
    <fo:block font-size="9pt" line-height="11pt"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <!-- ======================================================================== lists -->

  <xsl:template match="randomList">
    <fo:list-block space-before="4pt" space-after="6pt" start-indent="6mm"
      provisional-distance-between-starts="9mm" provisional-label-separation="3mm">
      <xsl:apply-templates/>
    </fo:list-block>
  </xsl:template>

  <xsl:template match="listItem">
    <fo:list-item space-after="3pt">
      <fo:list-item-label end-indent="label-end()">
        <fo:block font-size="10pt">
          <xsl:text>(</xsl:text><xsl:number format="a"/><xsl:text>)</xsl:text>
        </fo:block>
      </fo:list-item-label>
      <fo:list-item-body start-indent="body-start()">
        <xsl:apply-templates/>
      </fo:list-item-body>
    </fo:list-item>
  </xsl:template>

  <!-- listItem paragraphs sit at the body-start indent the list block established. -->
  <xsl:template match="listItem/para">
    <fo:block font-size="10pt" line-height="12.5pt"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <!-- ======================================================================= tables -->

  <xsl:template match="table">
    <fo:block space-before="8pt" space-after="10pt" start-indent="6mm">
      <xsl:apply-templates select="title"/>
      <xsl:apply-templates select="tgroup"/>
    </fo:block>
  </xsl:template>

  <xsl:template match="table/title">
    <fo:block font-size="9pt" font-weight="bold" space-after="4pt">
      <xsl:text>Table </xsl:text>
      <xsl:number count="table" level="any"/>
      <xsl:text>  </xsl:text>
      <xsl:value-of select="."/>
    </fo:block>
  </xsl:template>

  <xsl:template match="tgroup">
    <fo:table table-layout="fixed" width="100%" border-collapse="collapse">
      <xsl:apply-templates select="colspec"/>
      <xsl:apply-templates select="thead"/>
      <xsl:apply-templates select="tbody"/>
    </fo:table>
  </xsl:template>

  <xsl:template match="colspec">
    <fo:table-column>
      <xsl:attribute name="column-width"><xsl:value-of select="@colwidth"/></xsl:attribute>
    </fo:table-column>
  </xsl:template>

  <xsl:template match="thead">
    <fo:table-header><xsl:apply-templates/></fo:table-header>
  </xsl:template>

  <xsl:template match="tbody">
    <fo:table-body><xsl:apply-templates/></fo:table-body>
  </xsl:template>

  <xsl:template match="row">
    <fo:table-row><xsl:apply-templates/></fo:table-row>
  </xsl:template>

  <xsl:template match="thead/row/entry">
    <fo:table-cell background-color="#d9d9d9"
      border-style="solid" border-width="0.5pt" padding="3pt">
      <fo:block font-size="9pt" font-weight="bold"><xsl:apply-templates/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template match="tbody/row/entry">
    <fo:table-cell border-style="solid" border-width="0.4pt" padding="3pt">
      <fo:block font-size="9pt"><xsl:apply-templates/></fo:block>
    </fo:table-cell>
  </xsl:template>

  <!-- Cell paragraphs are already inside a block; do not open a second one. -->
  <xsl:template match="entry/para">
    <xsl:apply-templates/>
  </xsl:template>

</xsl:stylesheet>

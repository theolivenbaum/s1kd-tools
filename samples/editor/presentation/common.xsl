<?xml version="1.0" encoding="UTF-8"?>
<!--
  common.xsl — shared page furniture and common-construct presentation for every
  S1000D CSDB object type.

  The per-type stylesheets (proced.xsl, ipd.xsl, …) xsl:import this file and add
  templates for the content model of their own schema. Everything that S1000D
  shares between schemas lives here: the page masters, the running header and
  footer, the title block built from identAndStatusSection, the common
  block/inline constructs (paragraphs, levelled paragraphs, lists, CALS tables,
  figures, warnings, cautions, notes), the reference constructs (dmRef, pmRef,
  externalPubRef, internalRef) and the procedural constructs shared by the
  procedural, fault isolation, process, crew and checklist schemas.

  Layout follows the conventions of a page-oriented civil aircraft manual: the
  publisher wordmark and publication title in the header, the object code, issue
  and page count in the footer, ATA-style hierarchical step numbering
  (1. / A. / (1) / (a)), boxed warnings and cautions, and change bars in the
  start margin for change-marked content.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:output method="xml" indent="no" encoding="UTF-8"/>

  <!-- ============================ parameters ============================= -->

  <!-- Header, left. Empty means "take it from the object". -->
  <xsl:param name="publisher" select="''"/>
  <!--
    Path to an image of the publisher's mark, printed in the header in place of
    the wordmark set in type. A plain absolute path, not a file:// URI — the
    renderer resolves the former and draws an empty frame for the latter.
  -->
  <xsl:param name="publisher-logo" select="''"/>
  <!-- Height of that mark, in millimetres. -->
  <xsl:param name="publisher-logo-height" select="7"/>
  <!-- Header, right. -->
  <xsl:param name="publication-title" select="'TECHNICAL PUBLICATION'"/>

  <!-- Page geometry, in millimetres. -->
  <xsl:param name="page-width" select="210"/>
  <xsl:param name="page-height" select="297"/>
  <xsl:param name="margin-top" select="12"/>
  <xsl:param name="margin-bottom" select="12"/>
  <xsl:param name="margin-inner" select="20"/>
  <xsl:param name="margin-outer" select="15"/>

  <!-- Typography. -->
  <xsl:param name="font-family" select="'Helvetica'"/>
  <xsl:param name="mono-font-family" select="'Courier'"/>
  <xsl:param name="font-size" select="9"/>

  <!-- 1 prints the identification/status title block ahead of the content. -->
  <xsl:param name="title-block" select="1"/>
  <!-- Non-empty draws that text across every page. -->
  <xsl:param name="watermark" select="''"/>

  <!-- ============================= variables ============================= -->

  <xsl:variable name="fs" select="number($font-size)"/>
  <xsl:variable name="fs-small" select="$fs - 1"/>
  <xsl:variable name="fs-tiny" select="$fs - 1.75"/>
  <xsl:variable name="fs-title" select="$fs + 3"/>

  <xsl:variable name="body-w"
                select="number($page-width) - number($margin-inner) - number($margin-outer)"/>

  <xsl:variable name="header-extent" select="19"/>
  <xsl:variable name="footer-extent" select="14"/>

  <xsl:variable name="rule" select="'0.9pt solid black'"/>
  <xsl:variable name="cell-rule" select="'0.4pt solid #333333'"/>
  <xsl:variable name="shade" select="'#e4e4e4'"/>

  <!-- The object's own root, used by the header/footer/title block. -->
  <xsl:variable name="root" select="/*"/>

  <xsl:variable name="ident"
                select="$root/identAndStatusSection/dmAddress/dmIdent
                      | $root/identAndStatusSection/pmAddress/pmIdent
                      | $root/identAndStatusSection/dmlAddress/dmlIdent
                      | $root/identAndStatusSection/ddnAddress/ddnIdent
                      | $root/identAndStatusSection/commentAddress/commentIdent
                      | $root/identAndStatusSection/scormContentPackageAddress/scormContentPackageIdent
                      | $root/imfIdentAndStatusSection/imfAddress/imfIdent
                      | $root/updateIdentAndStatusSection/updateAddress/updateIdent"/>

  <xsl:variable name="address-items"
                select="$root/identAndStatusSection/dmAddress/dmAddressItems
                      | $root/identAndStatusSection/pmAddress/pmAddressItems
                      | $root/identAndStatusSection/dmlAddress/dmlAddressItems
                      | $root/identAndStatusSection/ddnAddress/ddnAddressItems
                      | $root/identAndStatusSection/commentAddress/commentAddressItems
                      | $root/identAndStatusSection/scormContentPackageAddress/scormContentPackageAddressItems
                      | $root/imfIdentAndStatusSection/imfAddress/imfAddressItems
                      | $root/updateIdentAndStatusSection/updateAddress/updateAddressItems"/>

  <xsl:variable name="status"
                select="$root/identAndStatusSection/dmStatus
                      | $root/identAndStatusSection/pmStatus
                      | $root/identAndStatusSection/dmlStatus
                      | $root/identAndStatusSection/ddnStatus
                      | $root/identAndStatusSection/commentStatus
                      | $root/identAndStatusSection/scormContentPackageStatus
                      | $root/imfIdentAndStatusSection/imfStatus
                      | $root/updateIdentAndStatusSection/updateStatus"/>

  <xsl:variable name="object-code">
    <xsl:call-template name="object-code"/>
  </xsl:variable>

  <xsl:variable name="publisher-name">
    <xsl:choose>
      <xsl:when test="string-length($publisher) &gt; 0">
        <xsl:value-of select="$publisher"/>
      </xsl:when>
      <xsl:when test="$status/responsiblePartnerCompany/enterpriseName">
        <xsl:value-of select="$status/responsiblePartnerCompany/enterpriseName"/>
      </xsl:when>
      <xsl:when test="$status/originator/enterpriseName">
        <xsl:value-of select="$status/originator/enterpriseName"/>
      </xsl:when>
      <!-- A dispatch note names no responsible partner; the sender is the
           nearest thing to a publisher it carries. -->
      <xsl:when test="$address-items/dispatchFrom/dispatchAddress/enterprise/enterpriseName">
        <xsl:value-of select="$address-items/dispatchFrom/dispatchAddress/enterprise/enterpriseName"/>
      </xsl:when>
      <xsl:when test="$address-items/commentOriginator/dispatchAddress/enterprise/enterpriseName">
        <xsl:value-of select="$address-items/commentOriginator/dispatchAddress/enterprise/enterpriseName"/>
      </xsl:when>
      <xsl:otherwise>
        <xsl:value-of select="$ident/*/@modelIdentCode"/>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:variable>

  <!-- ========================== page structure ========================== -->

  <xsl:template match="/">
    <fo:root font-family="{$font-family}" font-size="{$fs}pt" line-height="1.28"
             xml:lang="{$ident/language/@languageIsoCode}">
      <fo:layout-master-set>
        <fo:simple-page-master master-name="s1kd-page"
                               page-width="{$page-width}mm" page-height="{$page-height}mm"
                               margin-top="{$margin-top}mm" margin-bottom="{$margin-bottom}mm"
                               margin-left="{$margin-inner}mm" margin-right="{$margin-outer}mm">
          <fo:region-body margin-top="{$header-extent + 4}mm" margin-bottom="{$footer-extent + 3}mm"/>
          <fo:region-before extent="{$header-extent}mm"/>
          <fo:region-after extent="{$footer-extent}mm"/>
        </fo:simple-page-master>
      </fo:layout-master-set>

      <fo:page-sequence master-reference="s1kd-page">
        <xsl:call-template name="page-header"/>
        <xsl:call-template name="page-footer"/>
        <fo:flow flow-name="xsl-region-body">
          <xsl:if test="number($title-block) = 1">
            <xsl:call-template name="title-block"/>
          </xsl:if>
          <xsl:call-template name="document-body"/>
          <fo:block id="s1kd-last-page" space-before="0pt"/>
        </fo:flow>
      </fo:page-sequence>
    </fo:root>
  </xsl:template>

  <!--
    Everything after the identification and status section is content. Selecting
    it this way covers <content> (data modules, publication modules, SCORM
    packages, update files) as well as <dmlContent>, <ddnContent> and
    <commentContent> without a per-type root template.
  -->
  <xsl:template name="document-body">
    <xsl:apply-templates select="/*/*[not(self::identAndStatusSection)
                                  and not(self::imfIdentAndStatusSection)
                                  and not(self::updateIdentAndStatusSection)]"/>
  </xsl:template>

  <xsl:template match="content">
    <xsl:apply-templates/>
  </xsl:template>

  <xsl:template name="page-header">
    <fo:static-content flow-name="xsl-region-before">
      <xsl:if test="string-length($watermark) &gt; 0">
        <fo:block-container absolute-position="fixed"
                            left="0mm" top="{number($page-height) * 0.42}mm"
                            width="{$page-width}mm" height="30mm">
          <fo:block text-align="center" font-size="42pt" font-weight="bold"
                    color="#e0e0e0" letter-spacing="3pt">
            <xsl:value-of select="$watermark"/>
          </fo:block>
        </fo:block-container>
      </xsl:if>

      <fo:table table-layout="fixed" width="{$body-w}mm">
        <fo:table-column column-width="{$body-w * 0.42}mm"/>
        <fo:table-column column-width="{$body-w * 0.58}mm"/>
        <fo:table-body>
          <fo:table-row>
            <fo:table-cell>
              <xsl:choose>
                <!--
                  The real mark when one was given. A publisher's identity is a
                  drawn mark, not a name in the body face, and every other page
                  of the publication it belongs to carries it that way.
                -->
                <xsl:when test="string-length($publisher-logo) &gt; 0">
                  <fo:block>
                    <fo:external-graphic src="{$publisher-logo}"
                                         content-height="{$publisher-logo-height}mm"
                                         scaling="uniform"/>
                  </fo:block>
                  <!--
                    The mark says who published it; a name that says more than
                    that — a department, a site — is printed under it, small.
                  -->
                  <xsl:if test="string-length($publisher-name) &gt; 22">
                    <fo:block font-size="{$fs-tiny}pt" space-before="0.6mm">
                      <xsl:value-of select="$publisher-name"/>
                    </fo:block>
                  </xsl:if>
                </xsl:when>
                <!--
                  Otherwise the publisher is a wordmark when it is short and a
                  line of text when it is a full department name; sizing it by
                  length keeps a long name from pushing the header out of its
                  region.
                -->
                <xsl:otherwise>
                  <fo:block font-weight="bold">
                    <xsl:choose>
                      <xsl:when test="string-length($publisher-name) &gt; 40">
                        <xsl:attribute name="font-size"><xsl:value-of select="$fs"/>pt</xsl:attribute>
                      </xsl:when>
                      <xsl:when test="string-length($publisher-name) &gt; 22">
                        <xsl:attribute name="font-size"><xsl:value-of select="$fs + 2"/>pt</xsl:attribute>
                        <xsl:attribute name="letter-spacing">0.6pt</xsl:attribute>
                      </xsl:when>
                      <xsl:otherwise>
                        <xsl:attribute name="font-size"><xsl:value-of select="$fs + 5"/>pt</xsl:attribute>
                        <xsl:attribute name="letter-spacing">1.6pt</xsl:attribute>
                      </xsl:otherwise>
                    </xsl:choose>
                    <xsl:value-of select="$publisher-name"/>
                  </fo:block>
                </xsl:otherwise>
              </xsl:choose>
            </fo:table-cell>
            <fo:table-cell>
              <fo:block text-align="end" font-size="{$fs-small}pt" font-weight="bold"
                        letter-spacing="0.4pt">
                <xsl:value-of select="$publication-title"/>
              </fo:block>
              <fo:block text-align="end" font-size="{$fs-tiny}pt" space-before="0.6mm">
                <xsl:call-template name="model-and-language"/>
              </fo:block>
            </fo:table-cell>
          </fo:table-row>
          <fo:table-row>
            <fo:table-cell number-columns-spanned="2">
              <fo:block font-size="{$fs-tiny}pt" space-before="1mm">
                <fo:retrieve-marker retrieve-class-name="s1kd-section"
                                    retrieve-position="first-including-carryover"
                                    retrieve-boundary="page-sequence"/>
              </fo:block>
            </fo:table-cell>
          </fo:table-row>
        </fo:table-body>
      </fo:table>
      <fo:block border-bottom="{$rule}"/>
    </fo:static-content>
  </xsl:template>

  <xsl:template name="page-footer">
    <fo:static-content flow-name="xsl-region-after">
      <fo:block border-top="{$rule}" space-after="1.2mm"/>
      <xsl:if test="$status/security/@securityClassification and
                    $status/security/@securityClassification != '01'">
        <fo:block text-align="center" font-size="{$fs-tiny}pt" font-weight="bold"
                  space-after="1mm" letter-spacing="0.5pt">
          <xsl:call-template name="security-text">
            <xsl:with-param name="code" select="$status/security/@securityClassification"/>
          </xsl:call-template>
        </fo:block>
      </xsl:if>
      <fo:table table-layout="fixed" width="{$body-w}mm">
        <fo:table-column column-width="{$body-w * 0.52}mm"/>
        <fo:table-column column-width="{$body-w * 0.26}mm"/>
        <fo:table-column column-width="{$body-w * 0.22}mm"/>
        <fo:table-body>
          <fo:table-row>
            <fo:table-cell>
              <fo:block font-size="{$fs-tiny}pt"><xsl:value-of select="$object-code"/></fo:block>
            </fo:table-cell>
            <fo:table-cell>
              <fo:block font-size="{$fs-tiny}pt" text-align="center">
                <xsl:text>Issue </xsl:text>
                <xsl:call-template name="issue-string"/>
                <xsl:if test="$address-items/issueDate">
                  <xsl:text> · </xsl:text>
                  <xsl:call-template name="issue-date"/>
                </xsl:if>
              </fo:block>
            </fo:table-cell>
            <fo:table-cell>
              <fo:block font-size="{$fs-tiny}pt" text-align="end">
                <xsl:text>Page </xsl:text>
                <fo:page-number/>
                <xsl:text> of </xsl:text>
                <fo:page-number-citation-last ref-id="s1kd-last-page"/>
              </fo:block>
            </fo:table-cell>
          </fo:table-row>
        </fo:table-body>
      </fo:table>
    </fo:static-content>
  </xsl:template>

  <!-- ============================ title block =========================== -->

  <xsl:template name="title-block">
    <fo:block space-after="6mm" keep-together.within-page="always">
      <fo:block font-size="{$fs-title}pt" font-weight="bold" space-after="1mm">
        <fo:marker marker-class-name="s1kd-section">
          <xsl:call-template name="object-title"/>
        </fo:marker>
        <xsl:call-template name="object-title"/>
      </fo:block>
      <xsl:if test="$address-items/dmTitle/infoName | $address-items/dmTitle/infoNameVariant">
        <fo:block font-size="{$fs + 1}pt" space-after="3mm">
          <xsl:value-of select="$address-items/dmTitle/infoName"/>
          <xsl:if test="$address-items/dmTitle/infoNameVariant">
            <xsl:text> — </xsl:text>
            <xsl:value-of select="$address-items/dmTitle/infoNameVariant"/>
          </xsl:if>
        </fo:block>
      </xsl:if>

      <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
                space-before="2mm">
        <fo:table-column column-width="{$body-w * 0.24}mm"/>
        <fo:table-column column-width="{$body-w * 0.36}mm"/>
        <fo:table-column column-width="{$body-w * 0.16}mm"/>
        <fo:table-column column-width="{$body-w * 0.24}mm"/>
        <fo:table-body>
          <fo:table-row>
            <xsl:call-template name="ident-cell">
              <xsl:with-param name="label" select="'Object code'"/>
              <xsl:with-param name="value" select="$object-code"/>
            </xsl:call-template>
            <xsl:call-template name="ident-cell">
              <xsl:with-param name="label" select="'Issue'"/>
              <xsl:with-param name="value">
                <xsl:call-template name="issue-string"/>
                <xsl:if test="$status/@issueType">
                  <xsl:text> (</xsl:text>
                  <xsl:value-of select="$status/@issueType"/>
                  <xsl:text>)</xsl:text>
                </xsl:if>
              </xsl:with-param>
            </xsl:call-template>
          </fo:table-row>
          <fo:table-row>
            <xsl:call-template name="ident-cell">
              <xsl:with-param name="label" select="'Issue date'"/>
              <xsl:with-param name="value">
                <xsl:call-template name="issue-date"/>
              </xsl:with-param>
            </xsl:call-template>
            <xsl:call-template name="ident-cell">
              <xsl:with-param name="label" select="'Language'"/>
              <xsl:with-param name="value">
                <xsl:call-template name="language-string"/>
              </xsl:with-param>
            </xsl:call-template>
          </fo:table-row>
          <fo:table-row>
            <xsl:call-template name="ident-cell">
              <xsl:with-param name="label" select="'Responsible partner'"/>
              <xsl:with-param name="value">
                <xsl:call-template name="enterprise">
                  <xsl:with-param name="node" select="$status/responsiblePartnerCompany"/>
                </xsl:call-template>
              </xsl:with-param>
            </xsl:call-template>
            <xsl:call-template name="ident-cell">
              <xsl:with-param name="label" select="'Originator'"/>
              <xsl:with-param name="value">
                <xsl:call-template name="enterprise">
                  <xsl:with-param name="node" select="$status/originator"/>
                </xsl:call-template>
              </xsl:with-param>
            </xsl:call-template>
          </fo:table-row>
          <fo:table-row>
            <xsl:call-template name="ident-cell">
              <xsl:with-param name="label" select="'Security'"/>
              <xsl:with-param name="value">
                <xsl:call-template name="security-text">
                  <xsl:with-param name="code" select="$status/security/@securityClassification"/>
                </xsl:call-template>
              </xsl:with-param>
            </xsl:call-template>
            <xsl:call-template name="ident-cell">
              <xsl:with-param name="label" select="'Quality assurance'"/>
              <xsl:with-param name="value">
                <xsl:call-template name="quality-text"/>
              </xsl:with-param>
            </xsl:call-template>
          </fo:table-row>
          <fo:table-row>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
              <fo:block font-size="{$fs-small}pt" font-weight="bold">Applicability</fo:block>
            </fo:table-cell>
            <fo:table-cell border="{$cell-rule}" padding="1.2mm" number-columns-spanned="3">
              <fo:block font-size="{$fs-small}pt">
                <xsl:choose>
                  <xsl:when test="$status/applic/displayText">
                    <xsl:apply-templates select="$status/applic/displayText/simplePara"
                                         mode="plain"/>
                  </xsl:when>
                  <xsl:otherwise>All</xsl:otherwise>
                </xsl:choose>
              </fo:block>
            </fo:table-cell>
          </fo:table-row>
        </fo:table-body>
      </fo:table>
    </fo:block>
  </xsl:template>

  <xsl:template name="ident-cell">
    <xsl:param name="label"/>
    <xsl:param name="value"/>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
      <fo:block font-size="{$fs-small}pt" font-weight="bold"><xsl:value-of select="$label"/></fo:block>
    </fo:table-cell>
    <fo:table-cell border="{$cell-rule}" padding="1.2mm">
      <fo:block font-size="{$fs-small}pt">
        <xsl:choose>
          <xsl:when test="string-length(normalize-space($value)) &gt; 0">
            <xsl:value-of select="$value"/>
          </xsl:when>
          <xsl:otherwise>—</xsl:otherwise>
        </xsl:choose>
      </fo:block>
    </fo:table-cell>
  </xsl:template>

  <!-- ======================= identification strings ===================== -->

  <!-- The printable code of the object: DMC, PMC, DML, DDN, COM, ICN, SMC. -->
  <xsl:template name="object-code">
    <xsl:choose>
      <xsl:when test="$ident/dmCode">
        <xsl:variable name="c" select="$ident/dmCode"/>
        <xsl:text>DMC-</xsl:text>
        <xsl:value-of select="$c/@modelIdentCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@systemDiffCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@systemCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@subSystemCode"/>
        <xsl:value-of select="$c/@subSubSystemCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@assyCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@disassyCode"/>
        <xsl:value-of select="$c/@disassyCodeVariant"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@infoCode"/>
        <xsl:value-of select="$c/@infoCodeVariant"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@itemLocationCode"/>
        <xsl:if test="$c/@learnCode">
          <xsl:text>-</xsl:text><xsl:value-of select="$c/@learnCode"/>
          <xsl:value-of select="$c/@learnEventCode"/>
        </xsl:if>
      </xsl:when>
      <xsl:when test="$ident/pmCode">
        <xsl:variable name="c" select="$ident/pmCode"/>
        <xsl:text>PMC-</xsl:text>
        <xsl:value-of select="$c/@modelIdentCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@pmIssuer"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@pmNumber"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@pmVolume"/>
      </xsl:when>
      <xsl:when test="$ident/dmlCode">
        <xsl:variable name="c" select="$ident/dmlCode"/>
        <xsl:text>DML-</xsl:text>
        <xsl:value-of select="$c/@modelIdentCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@senderIdent"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@dmlType"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@yearOfDataIssue"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@seqNumber"/>
      </xsl:when>
      <xsl:when test="$ident/ddnCode">
        <xsl:variable name="c" select="$ident/ddnCode"/>
        <xsl:text>DDN-</xsl:text>
        <xsl:value-of select="$c/@modelIdentCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@senderIdent"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@receiverIdent"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@yearOfDataIssue"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@seqNumber"/>
      </xsl:when>
      <xsl:when test="$ident/commentCode">
        <xsl:variable name="c" select="$ident/commentCode"/>
        <xsl:text>COM-</xsl:text>
        <xsl:value-of select="$c/@modelIdentCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@senderIdent"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@yearOfDataIssue"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@seqNumber"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@commentType"/>
      </xsl:when>
      <xsl:when test="$ident/scormContentPackageCode">
        <xsl:variable name="c" select="$ident/scormContentPackageCode"/>
        <xsl:text>SMC-</xsl:text>
        <xsl:value-of select="$c/@modelIdentCode"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@scormContentPackageIssuer"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@scormContentPackageNumber"/>
        <xsl:text>-</xsl:text><xsl:value-of select="$c/@scormContentPackageVolume"/>
      </xsl:when>
      <xsl:when test="$ident/imfCode">
        <xsl:value-of select="$ident/imfCode/@imfIdentIcn"/>
      </xsl:when>
      <xsl:when test="$ident/updateCode">
        <xsl:text>UPF-</xsl:text>
        <xsl:value-of select="$ident/updateCode/@modelIdentCode"/>
        <xsl:text>-</xsl:text>
        <xsl:value-of select="$ident/updateCode/@senderIdent"/>
        <xsl:text>-</xsl:text>
        <xsl:value-of select="$ident/updateCode/@yearOfDataIssue"/>
        <xsl:text>-</xsl:text>
        <xsl:value-of select="$ident/updateCode/@seqNumber"/>
      </xsl:when>
      <xsl:otherwise>
        <xsl:value-of select="local-name($root)"/>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <!-- The object's headline: technical name, publication title, ICN title, … -->
  <xsl:template name="object-title">
    <xsl:choose>
      <xsl:when test="$address-items/dmTitle/techName">
        <xsl:value-of select="$address-items/dmTitle/techName"/>
      </xsl:when>
      <xsl:when test="$address-items/pmTitle">
        <xsl:value-of select="$address-items/pmTitle"/>
      </xsl:when>
      <xsl:when test="$address-items/icnTitle">
        <xsl:value-of select="$address-items/icnTitle"/>
      </xsl:when>
      <xsl:when test="$address-items/scormContentPackageTitle">
        <xsl:value-of select="$address-items/scormContentPackageTitle"/>
      </xsl:when>
      <xsl:otherwise>
        <xsl:value-of select="$publication-title"/>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template name="model-and-language">
    <xsl:value-of select="$ident/*/@modelIdentCode"/>
    <xsl:if test="$ident/language/@languageIsoCode">
      <xsl:text> · </xsl:text>
      <xsl:call-template name="language-string"/>
    </xsl:if>
  </xsl:template>

  <xsl:template name="language-string">
    <xsl:value-of select="$ident/language/@languageIsoCode"/>
    <xsl:if test="$ident/language/@countryIsoCode">
      <xsl:text>-</xsl:text>
      <xsl:value-of select="$ident/language/@countryIsoCode"/>
    </xsl:if>
  </xsl:template>

  <xsl:template name="issue-string">
    <xsl:choose>
      <xsl:when test="$ident/issueInfo/@issueNumber">
        <xsl:value-of select="$ident/issueInfo/@issueNumber"/>
        <xsl:text>-</xsl:text>
        <xsl:value-of select="$ident/issueInfo/@inWork"/>
      </xsl:when>
      <xsl:otherwise>—</xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template name="issue-date">
    <xsl:call-template name="format-date">
      <xsl:with-param name="date" select="$address-items/issueDate"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template name="format-date">
    <xsl:param name="date"/>
    <xsl:if test="$date/@year">
      <xsl:value-of select="$date/@year"/>
      <xsl:text>-</xsl:text>
      <xsl:value-of select="$date/@month"/>
      <xsl:text>-</xsl:text>
      <xsl:value-of select="$date/@day"/>
    </xsl:if>
  </xsl:template>

  <xsl:template name="enterprise">
    <xsl:param name="node"/>
    <xsl:value-of select="$node/enterpriseName"/>
    <xsl:if test="$node/@enterpriseCode">
      <xsl:text> (</xsl:text>
      <xsl:value-of select="$node/@enterpriseCode"/>
      <xsl:text>)</xsl:text>
    </xsl:if>
  </xsl:template>

  <xsl:template name="security-text">
    <xsl:param name="code"/>
    <xsl:choose>
      <xsl:when test="$code = '01'">01 — Unclassified</xsl:when>
      <xsl:when test="$code = '02'">02 — Restricted</xsl:when>
      <xsl:when test="$code = '03'">03 — Confidential</xsl:when>
      <xsl:when test="$code = '04'">04 — Secret</xsl:when>
      <xsl:when test="$code = '05'">05 — Top secret</xsl:when>
      <xsl:when test="$code">
        <xsl:value-of select="$code"/>
      </xsl:when>
      <xsl:otherwise>01 — Unclassified</xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template name="quality-text">
    <xsl:choose>
      <xsl:when test="$status/qualityAssurance/unverified">Unverified</xsl:when>
      <xsl:when test="$status/qualityAssurance/firstVerification">
        <xsl:text>First verification (</xsl:text>
        <xsl:value-of select="$status/qualityAssurance/firstVerification/@verificationType"/>
        <xsl:text>)</xsl:text>
      </xsl:when>
      <xsl:when test="$status/qualityAssurance/secondVerification">
        <xsl:text>Second verification (</xsl:text>
        <xsl:value-of select="$status/qualityAssurance/secondVerification/@verificationType"/>
        <xsl:text>)</xsl:text>
      </xsl:when>
    </xsl:choose>
  </xsl:template>

  <!-- ========================= section headings ========================= -->

  <!--
    A numbered top-level heading. Type stylesheets call this for the fixed
    sections of their schema (e.g. "Job set-up information" in a procedure) so
    every schema numbers its sections the same way.
  -->
  <xsl:template name="section-heading">
    <xsl:param name="number"/>
    <xsl:param name="text"/>
    <fo:block font-size="{$fs + 1}pt" font-weight="bold" space-before="5mm" space-after="2mm"
              keep-with-next.within-page="always">
      <fo:marker marker-class-name="s1kd-section">
        <xsl:if test="string-length($number) &gt; 0">
          <xsl:value-of select="$number"/><xsl:text> </xsl:text>
        </xsl:if>
        <xsl:value-of select="$text"/>
      </fo:marker>
      <xsl:if test="string-length($number) &gt; 0">
        <xsl:value-of select="$number"/><xsl:text>  </xsl:text>
      </xsl:if>
      <xsl:value-of select="$text"/>
    </fo:block>
  </xsl:template>

  <xsl:template name="subsection-heading">
    <xsl:param name="number"/>
    <xsl:param name="text"/>
    <fo:block font-weight="bold" space-before="3.5mm" space-after="1.5mm"
              keep-with-next.within-page="always">
      <xsl:if test="string-length($number) &gt; 0">
        <xsl:value-of select="$number"/><xsl:text>  </xsl:text>
      </xsl:if>
      <xsl:value-of select="$text"/>
    </fo:block>
  </xsl:template>

  <!-- =========================== block content ========================== -->

  <xsl:template match="para|simplePara|notePara|warningAndCautionPara|attentionListItemPara">
    <fo:block space-after="2mm">
      <xsl:call-template name="change-attributes"/>
      <xsl:call-template name="applicability-annotation"/>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="levelledPara">
    <fo:block space-before="3mm">
      <xsl:call-template name="change-attributes"/>
      <xsl:if test="title">
        <xsl:variable name="depth" select="count(ancestor-or-self::levelledPara)"/>
        <!-- The heading comes first, then the applicability statement that
             qualifies the whole section, as in a printed manual. -->
        <fo:block font-weight="bold" space-after="1.5mm"
                  keep-with-next.within-page="always">
          <xsl:attribute name="font-size">
            <xsl:choose>
              <xsl:when test="$depth = 1"><xsl:value-of select="$fs + 1"/>pt</xsl:when>
              <xsl:otherwise><xsl:value-of select="$fs"/>pt</xsl:otherwise>
            </xsl:choose>
          </xsl:attribute>
          <xsl:if test="$depth = 1">
            <fo:marker marker-class-name="s1kd-section">
              <xsl:number level="multiple" count="levelledPara" format="1.1.1.1 "/>
              <xsl:value-of select="title"/>
            </fo:marker>
          </xsl:if>
          <xsl:number level="multiple" count="levelledPara" format="1.1.1.1"/>
          <xsl:text>  </xsl:text>
          <xsl:apply-templates select="title" mode="inline"/>
        </fo:block>
      </xsl:if>
      <fo:block start-indent="{count(ancestor-or-self::levelledPara) * 3}mm">
        <xsl:call-template name="applicability-annotation"/>
        <xsl:apply-templates select="*[not(self::title)]"/>
      </fo:block>
    </fo:block>
  </xsl:template>

  <xsl:template match="title" mode="inline">
    <xsl:apply-templates/>
  </xsl:template>

  <!-- A stand-alone title (a section that is not a levelledPara). -->
  <xsl:template match="title">
    <fo:block font-weight="bold" space-before="3mm" space-after="1.5mm"
              keep-with-next.within-page="always">
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <!-- ============================== lists ============================== -->

  <xsl:template match="randomList">
    <fo:list-block provisional-distance-between-starts="6mm" provisional-label-separation="2mm"
                   space-before="1.5mm" space-after="2mm"
                   start-indent="{4 + 4 * count(ancestor::randomList|ancestor::sequentialList)}mm">
      <xsl:call-template name="change-attributes"/>
      <xsl:apply-templates select="listItem"/>
    </fo:list-block>
  </xsl:template>

  <xsl:template match="randomList/listItem">
    <fo:list-item space-after="1mm">
      <fo:list-item-label end-indent="label-end()">
        <fo:block>
          <xsl:choose>
            <xsl:when test="../@listItemPrefix = 'pf01'"/>
            <xsl:when test="../@listItemPrefix = 'pf02'">–</xsl:when>
            <xsl:when test="../@listItemPrefix = 'pf03'">•</xsl:when>
            <xsl:otherwise>•</xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:list-item-label>
      <fo:list-item-body start-indent="body-start()">
        <fo:block>
          <xsl:call-template name="applicability-annotation"/>
          <xsl:apply-templates/>
        </fo:block>
      </fo:list-item-body>
    </fo:list-item>
  </xsl:template>

  <xsl:template match="sequentialList">
    <fo:list-block provisional-distance-between-starts="8mm" provisional-label-separation="2mm"
                   space-before="1.5mm" space-after="2mm"
                   start-indent="{4 + 4 * count(ancestor::randomList|ancestor::sequentialList)}mm">
      <xsl:call-template name="change-attributes"/>
      <xsl:apply-templates select="listItem"/>
    </fo:list-block>
  </xsl:template>

  <xsl:template match="sequentialList/listItem">
    <xsl:variable name="depth" select="count(ancestor::sequentialList)"/>
    <fo:list-item space-after="1mm">
      <fo:list-item-label end-indent="label-end()">
        <fo:block>
          <xsl:choose>
            <xsl:when test="$depth = 1"><xsl:number format="1."/></xsl:when>
            <xsl:when test="$depth = 2"><xsl:number format="a."/></xsl:when>
            <xsl:otherwise><xsl:number format="i."/></xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:list-item-label>
      <fo:list-item-body start-indent="body-start()">
        <fo:block>
          <xsl:call-template name="applicability-annotation"/>
          <xsl:apply-templates/>
        </fo:block>
      </fo:list-item-body>
    </fo:list-item>
  </xsl:template>

  <xsl:template match="definitionList">
    <fo:table table-layout="fixed" width="{$body-w}mm" space-before="2mm" space-after="2mm"
              start-indent="4mm">
      <fo:table-column column-width="{$body-w * 0.3}mm"/>
      <fo:table-column column-width="{$body-w * 0.66}mm"/>
      <fo:table-body>
        <xsl:apply-templates select="definitionListItem"/>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template match="definitionListItem">
    <fo:table-row>
      <fo:table-cell padding-after="1.5mm" padding-end="2mm">
        <fo:block font-weight="bold"><xsl:apply-templates select="listItemTerm"/></fo:block>
      </fo:table-cell>
      <fo:table-cell padding-after="1.5mm">
        <fo:block><xsl:apply-templates select="listItemDefinition"/></fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <xsl:template match="listItemTerm|listItemDefinition">
    <xsl:apply-templates/>
  </xsl:template>

  <!-- ==================== warnings, cautions and notes ================== -->

  <xsl:template match="warning|caution">
    <fo:block border="0.8pt solid black" padding="2mm" space-before="3mm" space-after="3mm"
              start-indent="4mm" end-indent="4mm" keep-together.within-page="always">
      <xsl:call-template name="change-attributes"/>
      <fo:block font-weight="bold" text-align="center" letter-spacing="1.5pt" space-after="1.5mm">
        <xsl:choose>
          <xsl:when test="self::warning">WARNING</xsl:when>
          <xsl:otherwise>CAUTION</xsl:otherwise>
        </xsl:choose>
      </fo:block>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <xsl:template match="note">
    <fo:list-block provisional-distance-between-starts="14mm" provisional-label-separation="2mm"
                   space-before="2mm" space-after="2mm" start-indent="4mm">
      <xsl:call-template name="change-attributes"/>
      <fo:list-item>
        <fo:list-item-label end-indent="label-end()">
          <fo:block font-weight="bold">
            <xsl:choose>
              <xsl:when test="@noteType = 'other'">NOTE:</xsl:when>
              <xsl:otherwise>NOTE:</xsl:otherwise>
            </xsl:choose>
          </fo:block>
        </fo:list-item-label>
        <fo:list-item-body start-indent="body-start()">
          <fo:block><xsl:apply-templates/></fo:block>
        </fo:list-item-body>
      </fo:list-item>
    </fo:list-block>
  </xsl:template>

  <xsl:template match="attention">
    <fo:block border="0.8pt solid black" padding="2mm" space-before="3mm" space-after="3mm"
              start-indent="4mm" end-indent="4mm" keep-together.within-page="always">
      <fo:block font-weight="bold" text-align="center" letter-spacing="1.5pt" space-after="1.5mm">
        ATTENTION
      </fo:block>
      <xsl:apply-templates/>
    </fo:block>
  </xsl:template>

  <!-- References to a warning or caution held in a common information repository. -->
  <xsl:template match="warningRef|cautionRef">
    <fo:block font-weight="bold" space-before="2mm" space-after="2mm" start-indent="4mm">
      <xsl:choose>
        <xsl:when test="self::warningRef">WARNING </xsl:when>
        <xsl:otherwise>CAUTION </xsl:otherwise>
      </xsl:choose>
      <xsl:text>(Ref. </xsl:text>
      <xsl:value-of select="warningIdentNumber|cautionIdentNumber|@warningIdentNumber|@cautionIdentNumber"/>
      <xsl:text>)</xsl:text>
    </fo:block>
  </xsl:template>

  <!-- ============================= figures ============================= -->

  <!--
    A figure is not marked keep-together: an illustration is already unbreakable,
    and keeping the enclosing block together would drag the whole procedural step
    that contains it onto the next page. The caption is tied to the illustration
    with keep-with-previous instead.
  -->
  <xsl:template match="figure|foldout">
    <fo:block space-before="4mm" space-after="4mm" text-align="center">
      <xsl:call-template name="change-attributes"/>
      <xsl:apply-templates select="graphic|multimediaObject"/>
      <fo:block font-weight="bold" font-size="{$fs-small}pt" space-before="2mm"
                keep-with-previous.within-page="always">
        <xsl:if test="title">
          <xsl:apply-templates select="title" mode="inline"/>
          <xsl:text> — </xsl:text>
        </xsl:if>
        <xsl:text>Figure </xsl:text>
        <xsl:number level="any" count="figure|foldout" format="1"/>
      </fo:block>
    </fo:block>
  </xsl:template>

  <xsl:template match="graphic|multimediaObject">
    <xsl:choose>
      <xsl:when test="@s1kdResolvedGraphic">
        <!--
          Width-constrained only. Giving the viewport a height as well reserves that
          height whatever the illustration measures, so a wide, shallow figure asks
          the page for room it does not use and is pushed to the next one, leaving
          the bottom third of a page empty. Constraining the width and letting the
          uniform scale set the height reserves what the figure actually occupies.
        -->
        <fo:block>
          <fo:external-graphic src="url('{@s1kdResolvedGraphic}')"
                               content-width="scale-down-to-fit"
                               width="{$body-w - 10}mm"
                               scaling="uniform"/>
        </fo:block>
      </xsl:when>
      <xsl:otherwise>
        <!-- The ICN was not supplied to the renderer: keep the page honest by
             reserving the space and naming the missing entity. -->
        <fo:block-container width="{$body-w * 0.7}mm" height="45mm"
                            border="0.4pt dashed #999999" margin-left="{$body-w * 0.15}mm">
          <fo:block space-before="18mm" text-align="center" font-size="{$fs-tiny}pt"
                    color="#666666" font-style="italic">
            <xsl:value-of select="@infoEntityIdent"/>
            <xsl:if test="not(@infoEntityIdent)">graphic not supplied</xsl:if>
          </fo:block>
        </fo:block-container>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <!-- ============================ CALS tables =========================== -->

  <xsl:template match="table">
    <fo:block space-before="3.5mm" space-after="3.5mm">
      <xsl:call-template name="change-attributes"/>
      <xsl:call-template name="applicability-annotation"/>
      <xsl:if test="title">
        <fo:block font-weight="bold" font-size="{$fs-small}pt" space-after="1.5mm"
                  keep-with-next.within-page="always">
          <xsl:text>Table </xsl:text>
          <xsl:number level="any" count="table" format="1"/>
          <xsl:text>  </xsl:text>
          <xsl:apply-templates select="title" mode="inline"/>
        </fo:block>
      </xsl:if>
      <xsl:apply-templates select="tgroup"/>
    </fo:block>
  </xsl:template>

  <xsl:template match="tgroup">
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt">
      <xsl:choose>
        <xsl:when test="colspec">
          <xsl:apply-templates select="colspec"/>
        </xsl:when>
        <xsl:otherwise>
          <xsl:call-template name="even-columns">
            <xsl:with-param name="n" select="number(@cols)"/>
          </xsl:call-template>
        </xsl:otherwise>
      </xsl:choose>
      <xsl:apply-templates select="thead"/>
      <xsl:apply-templates select="tbody"/>
      <xsl:apply-templates select="tfoot"/>
    </fo:table>
  </xsl:template>

  <xsl:template name="even-columns">
    <xsl:param name="n"/>
    <xsl:if test="$n &gt; 0">
      <fo:table-column column-width="proportional-column-width(1)"/>
      <xsl:call-template name="even-columns">
        <xsl:with-param name="n" select="$n - 1"/>
      </xsl:call-template>
    </xsl:if>
  </xsl:template>

  <xsl:template match="colspec">
    <fo:table-column>
      <xsl:attribute name="column-width">
        <xsl:choose>
          <xsl:when test="not(@colwidth) or @colwidth = '*'">proportional-column-width(1)</xsl:when>
          <xsl:when test="contains(@colwidth, '*')">
            <xsl:text>proportional-column-width(</xsl:text>
            <xsl:choose>
              <xsl:when test="substring-before(@colwidth, '*') = ''">1</xsl:when>
              <xsl:otherwise><xsl:value-of select="substring-before(@colwidth, '*')"/></xsl:otherwise>
            </xsl:choose>
            <xsl:text>)</xsl:text>
          </xsl:when>
          <xsl:otherwise><xsl:value-of select="@colwidth"/></xsl:otherwise>
        </xsl:choose>
      </xsl:attribute>
    </fo:table-column>
  </xsl:template>

  <xsl:template match="thead">
    <fo:table-header>
      <xsl:apply-templates select="row"/>
    </fo:table-header>
  </xsl:template>

  <xsl:template match="tbody">
    <fo:table-body>
      <xsl:apply-templates select="row"/>
    </fo:table-body>
  </xsl:template>

  <xsl:template match="tfoot">
    <fo:table-footer>
      <xsl:apply-templates select="row"/>
    </fo:table-footer>
  </xsl:template>

  <xsl:template match="row">
    <fo:table-row>
      <xsl:call-template name="applicability-row-shading"/>
      <xsl:apply-templates select="entry"/>
    </fo:table-row>
  </xsl:template>

  <xsl:template name="applicability-row-shading"/>

  <xsl:template match="entry">
    <fo:table-cell padding="1.2mm">
      <xsl:if test="not(ancestor::tgroup/../@frame = 'none')">
        <xsl:attribute name="border"><xsl:value-of select="$cell-rule"/></xsl:attribute>
      </xsl:if>
      <xsl:if test="ancestor::thead">
        <xsl:attribute name="background-color"><xsl:value-of select="$shade"/></xsl:attribute>
      </xsl:if>
      <xsl:if test="@morerows">
        <xsl:attribute name="number-rows-spanned"><xsl:value-of select="@morerows + 1"/></xsl:attribute>
      </xsl:if>
      <xsl:if test="@namest and @nameend">
        <xsl:attribute name="number-columns-spanned">
          <xsl:call-template name="column-span"/>
        </xsl:attribute>
      </xsl:if>
      <fo:block>
        <xsl:if test="ancestor::thead">
          <xsl:attribute name="font-weight">bold</xsl:attribute>
        </xsl:if>
        <xsl:if test="@align">
          <xsl:attribute name="text-align">
            <xsl:choose>
              <xsl:when test="@align = 'left'">start</xsl:when>
              <xsl:when test="@align = 'right'">end</xsl:when>
              <xsl:otherwise><xsl:value-of select="@align"/></xsl:otherwise>
            </xsl:choose>
          </xsl:attribute>
        </xsl:if>
        <xsl:apply-templates/>
      </fo:block>
    </fo:table-cell>
  </xsl:template>

  <xsl:template name="column-span">
    <xsl:variable name="tg" select="ancestor::tgroup[1]"/>
    <xsl:variable name="start" select="count($tg/colspec[@colname = current()/@namest]/preceding-sibling::colspec) + 1"/>
    <xsl:variable name="end" select="count($tg/colspec[@colname = current()/@nameend]/preceding-sibling::colspec) + 1"/>
    <xsl:value-of select="$end - $start + 1"/>
  </xsl:template>

  <!-- ========================= inline constructs ======================== -->

  <xsl:template match="emphasis">
    <fo:inline>
      <xsl:choose>
        <xsl:when test="@emphasisType = 'em02'">
          <xsl:attribute name="font-style">italic</xsl:attribute>
        </xsl:when>
        <xsl:when test="@emphasisType = 'em03'">
          <xsl:attribute name="text-decoration">underline</xsl:attribute>
        </xsl:when>
        <xsl:when test="@emphasisType = 'em04'">
          <xsl:attribute name="text-decoration">overline</xsl:attribute>
        </xsl:when>
        <xsl:when test="@emphasisType = 'em05'">
          <xsl:attribute name="font-weight">bold</xsl:attribute>
          <xsl:attribute name="font-style">italic</xsl:attribute>
        </xsl:when>
        <xsl:otherwise>
          <xsl:attribute name="font-weight">bold</xsl:attribute>
        </xsl:otherwise>
      </xsl:choose>
      <xsl:apply-templates/>
    </fo:inline>
  </xsl:template>

  <xsl:template match="verbatimText">
    <fo:inline font-family="{$mono-font-family}" font-size="{$fs-small}pt"
               background-color="#f0f0f0">
      <xsl:apply-templates/>
    </fo:inline>
  </xsl:template>

  <xsl:template match="subScript">
    <fo:inline font-size="{$fs - 2.5}pt" baseline-shift="sub"><xsl:apply-templates/></fo:inline>
  </xsl:template>

  <xsl:template match="superScript">
    <fo:inline font-size="{$fs - 2.5}pt" baseline-shift="super"><xsl:apply-templates/></fo:inline>
  </xsl:template>

  <xsl:template match="acronym">
    <xsl:value-of select="acronymTerm"/>
  </xsl:template>

  <xsl:template match="footnote">
    <fo:footnote>
      <fo:inline font-size="{$fs-tiny}pt" baseline-shift="super">
        <xsl:number level="any" count="footnote" format="1"/>
      </fo:inline>
      <fo:footnote-body>
        <fo:block font-size="{$fs-tiny}pt" space-before="1mm" start-indent="4mm">
          <fo:inline baseline-shift="super">
            <xsl:number level="any" count="footnote" format="1"/>
          </fo:inline>
          <xsl:text> </xsl:text>
          <xsl:apply-templates/>
        </fo:block>
      </fo:footnote-body>
    </fo:footnote>
  </xsl:template>

  <!-- Quantities, part numbers and other typed inline data. -->
  <xsl:template match="quantity">
    <xsl:value-of select="quantityGroup/quantityValue"/>
    <xsl:if test="quantityGroup/quantityValue/@quantityUnitOfMeasure">
      <xsl:text> </xsl:text>
      <xsl:value-of select="quantityGroup/quantityValue/@quantityUnitOfMeasure"/>
    </xsl:if>
  </xsl:template>

  <!-- ========================== reference inlines ======================= -->

  <!--
    The reference inlines deliberately emit plain text rather than an fo:inline:
    the renderer opens a word boundary at every inline edge, which would put a
    space in front of the punctuation that so often follows a reference.
  -->
  <xsl:template match="dmRef">
    <xsl:text>(Ref. </xsl:text>
    <xsl:choose>
      <xsl:when test="dmRefAddressItems/dmTitle/techName">
        <xsl:value-of select="dmRefAddressItems/dmTitle/techName"/>
        <xsl:if test="dmRefAddressItems/dmTitle/infoName">
          <xsl:text> — </xsl:text>
          <xsl:value-of select="dmRefAddressItems/dmTitle/infoName"/>
        </xsl:if>
      </xsl:when>
      <xsl:otherwise>
        <xsl:call-template name="dm-code-string">
          <xsl:with-param name="c" select="dmRefIdent/dmCode"/>
        </xsl:call-template>
      </xsl:otherwise>
    </xsl:choose>
    <xsl:text>)</xsl:text>
  </xsl:template>

  <xsl:template match="pmRef">
    <xsl:text>(Ref. </xsl:text>
    <xsl:choose>
      <xsl:when test="pmRefAddressItems/pmTitle">
        <xsl:value-of select="pmRefAddressItems/pmTitle"/>
      </xsl:when>
      <xsl:otherwise>
        <xsl:call-template name="pm-code-string">
          <xsl:with-param name="c" select="pmRefIdent/pmCode"/>
        </xsl:call-template>
      </xsl:otherwise>
    </xsl:choose>
    <xsl:text>)</xsl:text>
  </xsl:template>

  <xsl:template match="externalPubRef">
    <xsl:text>(Ref. </xsl:text>
    <xsl:choose>
      <xsl:when test="externalPubRefAddressItems/externalPubTitle">
        <xsl:value-of select="externalPubRefAddressItems/externalPubTitle"/>
      </xsl:when>
      <xsl:otherwise>
        <xsl:value-of select="externalPubRefIdent/externalPubCode"/>
      </xsl:otherwise>
    </xsl:choose>
    <xsl:text>)</xsl:text>
  </xsl:template>

  <!-- An internal reference prints the way the target is numbered on the page:
       "step 3.A", "Table 2", "Figure 1" — not the raw XML id. -->
  <xsl:template match="internalRef">
    <xsl:variable name="target" select="//*[@id = current()/@internalRefId]"/>
    <xsl:choose>
      <xsl:when test="$target/self::proceduralStep or $target/self::isolationStep
                   or $target/self::crewDrillStep">
        <xsl:text>step </xsl:text>
        <xsl:for-each select="$target">
          <xsl:call-template name="step-number"/>
        </xsl:for-each>
      </xsl:when>
      <xsl:when test="$target/self::table">
        <xsl:text>Table </xsl:text>
        <xsl:for-each select="$target">
          <xsl:number level="any" count="table" format="1"/>
        </xsl:for-each>
      </xsl:when>
      <xsl:when test="$target/self::figure or $target/self::foldout">
        <xsl:text>Figure </xsl:text>
        <xsl:for-each select="$target">
          <xsl:number level="any" count="figure|foldout" format="1"/>
        </xsl:for-each>
      </xsl:when>
      <xsl:when test="$target/title">
        <xsl:value-of select="$target/title"/>
      </xsl:when>
      <xsl:otherwise>
        <xsl:value-of select="@internalRefId"/>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <!-- The DMC of a referenced data module, without the enclosing brackets. -->
  <xsl:template name="dm-code-string">
    <xsl:param name="c"/>
    <xsl:text>DMC-</xsl:text>
    <xsl:value-of select="$c/@modelIdentCode"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@systemDiffCode"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@systemCode"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@subSystemCode"/>
    <xsl:value-of select="$c/@subSubSystemCode"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@assyCode"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@disassyCode"/>
    <xsl:value-of select="$c/@disassyCodeVariant"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@infoCode"/>
    <xsl:value-of select="$c/@infoCodeVariant"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@itemLocationCode"/>
  </xsl:template>

  <xsl:template name="pm-code-string">
    <xsl:param name="c"/>
    <xsl:text>PMC-</xsl:text>
    <xsl:value-of select="$c/@modelIdentCode"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@pmIssuer"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@pmNumber"/>
    <xsl:text>-</xsl:text><xsl:value-of select="$c/@pmVolume"/>
  </xsl:template>

  <!-- ===================== applicability and change marks =============== -->

  <!--
    Print the applicability statement carried by @applicRefId. S1000D holds the
    statement once in referencedApplicGroup and points at it from every element
    it qualifies, which is how one data module covers several configurations.
  -->
  <xsl:template name="applicability-annotation">
    <xsl:if test="@applicRefId">
      <xsl:variable name="applic"
                    select="//referencedApplicGroup/applic[@id = current()/@applicRefId]"/>
      <xsl:if test="$applic">
        <fo:block font-size="{$fs-small}pt" font-weight="bold" space-after="0.8mm">
          <xsl:text>APPLICABLE TO: </xsl:text>
          <xsl:choose>
            <xsl:when test="$applic/displayText">
              <xsl:apply-templates select="$applic/displayText/simplePara" mode="plain"/>
            </xsl:when>
            <xsl:otherwise>
              <xsl:apply-templates select="$applic/assert|$applic/evaluate" mode="applic"/>
            </xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </xsl:if>
    </xsl:if>
  </xsl:template>

  <!-- displayText rendered as running text rather than as its own block. -->
  <xsl:template match="simplePara" mode="plain">
    <xsl:apply-templates/>
  </xsl:template>

  <!-- A last-resort rendering of a computable applicability expression. -->
  <xsl:template match="assert" mode="applic">
    <xsl:value-of select="@applicPropertyIdent"/>
    <xsl:text> </xsl:text>
    <xsl:value-of select="@applicPropertyValues"/>
    <xsl:if test="following-sibling::assert"> and </xsl:if>
  </xsl:template>

  <xsl:template match="evaluate" mode="applic">
    <xsl:apply-templates select="assert|evaluate" mode="applic"/>
  </xsl:template>

  <!--
    Change marks. S1000D marks changed content with @changeMark/@changeType;
    printed manuals show that as a change bar in the start margin, which is what
    these attributes produce on the calling block.
  -->
  <xsl:template name="change-attributes">
    <xsl:if test="@changeMark = '1' or @changeMark = 'changeMark'">
      <xsl:attribute name="border-start-width">1.6pt</xsl:attribute>
      <xsl:attribute name="border-start-style">solid</xsl:attribute>
      <xsl:attribute name="border-start-color">black</xsl:attribute>
      <xsl:attribute name="padding-start">2mm</xsl:attribute>
    </xsl:if>
  </xsl:template>

  <!-- ===================== shared procedural constructs ================= -->

  <!-- 1. / A. / (1) / (a) / 1) / a) — the classic ATA step hierarchy. -->
  <xsl:template name="step-number">
    <xsl:variable name="depth"
                  select="count(ancestor-or-self::proceduralStep
                              | ancestor-or-self::isolationStep
                              | ancestor-or-self::crewDrillStep)"/>
    <xsl:choose>
      <xsl:when test="$depth = 1">
        <xsl:number count="proceduralStep|isolationStep|crewDrillStep" format="1."/>
      </xsl:when>
      <xsl:when test="$depth = 2">
        <xsl:number count="proceduralStep|isolationStep|crewDrillStep" format="A."/>
      </xsl:when>
      <xsl:when test="$depth = 3">
        <xsl:text>(</xsl:text>
        <xsl:number count="proceduralStep|isolationStep|crewDrillStep" format="1"/>
        <xsl:text>)</xsl:text>
      </xsl:when>
      <xsl:when test="$depth = 4">
        <xsl:text>(</xsl:text>
        <xsl:number count="proceduralStep|isolationStep|crewDrillStep" format="a"/>
        <xsl:text>)</xsl:text>
      </xsl:when>
      <xsl:when test="$depth = 5">
        <xsl:number count="proceduralStep|isolationStep|crewDrillStep" format="1"/>
        <xsl:text>)</xsl:text>
      </xsl:when>
      <xsl:otherwise>
        <xsl:number count="proceduralStep|isolationStep|crewDrillStep" format="a"/>
        <xsl:text>)</xsl:text>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="proceduralStep|crewDrillStep">
    <xsl:variable name="depth"
                  select="count(ancestor-or-self::proceduralStep|ancestor-or-self::crewDrillStep)"/>
    <fo:list-block provisional-distance-between-starts="9mm" provisional-label-separation="2mm"
                   space-before="2mm" start-indent="{($depth - 1) * 9}mm">
      <xsl:call-template name="change-attributes"/>
      <fo:list-item>
        <fo:list-item-label end-indent="label-end()">
          <fo:block font-weight="bold"><xsl:call-template name="step-number"/></fo:block>
        </fo:list-item-label>
        <fo:list-item-body start-indent="body-start()">
          <fo:block>
            <xsl:if test="@id">
              <xsl:attribute name="id"><xsl:value-of select="@id"/></xsl:attribute>
            </xsl:if>
            <xsl:call-template name="applicability-annotation"/>
            <xsl:if test="title">
              <fo:block font-weight="bold" space-after="1mm">
                <xsl:apply-templates select="title" mode="inline"/>
              </fo:block>
            </xsl:if>
            <xsl:apply-templates select="*[not(self::title)]"/>
          </fo:block>
        </fo:list-item-body>
      </fo:list-item>
    </fo:list-block>
  </xsl:template>

  <!--
    Preliminary and closing requirements. These carry the job set-up information
    of a procedure — conditions, support equipment, consumables, spares and
    safety — and are shared by the procedural, fault isolation, process and crew
    schemas, so they are presented identically everywhere.
  -->
  <xsl:template name="preliminary-requirements">
    <xsl:param name="node"/>
    <xsl:param name="number" select="''"/>
    <xsl:param name="heading" select="'Job set-up information'"/>

    <xsl:call-template name="section-heading">
      <xsl:with-param name="number" select="$number"/>
      <xsl:with-param name="text" select="$heading"/>
    </xsl:call-template>

    <xsl:call-template name="requirement-block">
      <xsl:with-param name="label" select="'A.  Referenced information'"/>
      <xsl:with-param name="empty" select="boolean($node/reqCondGroup/noConds)"/>
      <xsl:with-param name="empty-text" select="'No conditions'"/>
      <xsl:with-param name="items" select="$node/reqCondGroup/reqCondDm|$node/reqCondGroup/reqCondPm|$node/reqCondGroup/reqCondNoRef"/>
      <xsl:with-param name="kind" select="'conds'"/>
    </xsl:call-template>

    <xsl:call-template name="requirement-block">
      <xsl:with-param name="label" select="'B.  Fixtures, tools, test and support equipment'"/>
      <xsl:with-param name="empty" select="boolean($node/reqSupportEquips/noSupportEquips)"/>
      <xsl:with-param name="empty-text" select="'No support equipment required'"/>
      <xsl:with-param name="items" select="$node/reqSupportEquips/supportEquipDescrGroup/supportEquipDescr"/>
      <xsl:with-param name="kind" select="'equip'"/>
    </xsl:call-template>

    <xsl:call-template name="requirement-block">
      <xsl:with-param name="label" select="'C.  Consumable materials'"/>
      <xsl:with-param name="empty" select="boolean($node/reqSupplies/noSupplies)"/>
      <xsl:with-param name="empty-text" select="'No consumable materials required'"/>
      <xsl:with-param name="items" select="$node/reqSupplies/supplyDescrGroup/supplyDescr"/>
      <xsl:with-param name="kind" select="'supply'"/>
    </xsl:call-template>

    <xsl:call-template name="requirement-block">
      <xsl:with-param name="label" select="'D.  Expendable parts'"/>
      <xsl:with-param name="empty" select="boolean($node/reqSpares/noSpares)"/>
      <xsl:with-param name="empty-text" select="'No expendable parts required'"/>
      <xsl:with-param name="items" select="$node/reqSpares/spareDescrGroup/spareDescr"/>
      <xsl:with-param name="kind" select="'spare'"/>
    </xsl:call-template>

    <xsl:if test="$node/reqSafety/safetyRqmts">
      <xsl:call-template name="subsection-heading">
        <xsl:with-param name="text" select="'E.  Safety conditions'"/>
      </xsl:call-template>
      <xsl:apply-templates select="$node/reqSafety/safetyRqmts/*"/>
    </xsl:if>

    <xsl:if test="$node/reqPersons">
      <xsl:call-template name="subsection-heading">
        <xsl:with-param name="text" select="'F.  Personnel'"/>
      </xsl:call-template>
      <xsl:apply-templates select="$node/reqPersons/*"/>
    </xsl:if>
  </xsl:template>

  <xsl:template name="requirement-block">
    <xsl:param name="label"/>
    <xsl:param name="empty"/>
    <xsl:param name="empty-text"/>
    <xsl:param name="items"/>
    <xsl:param name="kind"/>

    <xsl:call-template name="subsection-heading">
      <xsl:with-param name="text" select="$label"/>
    </xsl:call-template>

    <xsl:choose>
      <xsl:when test="$empty or not($items)">
        <fo:block start-indent="6mm" font-style="italic" space-after="1mm">
          <xsl:value-of select="$empty-text"/>
        </fo:block>
      </xsl:when>
      <xsl:otherwise>
        <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
                  font-size="{$fs-small}pt" space-after="2mm">
          <fo:table-column column-width="{$body-w * 0.14}mm"/>
          <fo:table-column column-width="{$body-w * 0.53}mm"/>
          <fo:table-column column-width="{$body-w * 0.33}mm"/>
          <fo:table-header>
            <fo:table-row>
              <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
                <fo:block font-weight="bold">
                  <xsl:choose>
                    <xsl:when test="$kind = 'conds'">TYPE</xsl:when>
                    <xsl:otherwise>QTY</xsl:otherwise>
                  </xsl:choose>
                </fo:block>
              </fo:table-cell>
              <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
                <fo:block font-weight="bold">DESIGNATION</fo:block>
              </fo:table-cell>
              <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
                <fo:block font-weight="bold">IDENTIFICATION No.</fo:block>
              </fo:table-cell>
            </fo:table-row>
          </fo:table-header>
          <fo:table-body>
            <xsl:apply-templates select="$items" mode="requirement"/>
          </fo:table-body>
        </fo:table>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="supportEquipDescr|supplyDescr|spareDescr" mode="requirement">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block text-align="center">
          <xsl:choose>
            <xsl:when test="reqQuantity"><xsl:value-of select="reqQuantity"/></xsl:when>
            <xsl:otherwise>1</xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:value-of select="name"/></fo:block>
        <xsl:if test="shortName">
          <fo:block font-size="{$fs-tiny}pt" color="#444444">
            <xsl:value-of select="shortName"/>
          </fo:block>
        </xsl:if>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:choose>
            <xsl:when test="identNumber/manufacturerCode or identNumber/partAndSerialNumber">
              <xsl:value-of select="identNumber/partAndSerialNumber/partNumber"/>
              <xsl:if test="identNumber/manufacturerCode">
                <xsl:text> (CAGE </xsl:text>
                <xsl:value-of select="identNumber/manufacturerCode"/>
                <xsl:text>)</xsl:text>
              </xsl:if>
            </xsl:when>
            <xsl:when test="natoStockNumber">
              <xsl:value-of select="natoStockNumber"/>
            </xsl:when>
            <xsl:otherwise>—</xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <xsl:template match="reqCondDm|reqCondPm|reqCondNoRef" mode="requirement">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:choose>
            <xsl:when test="self::reqCondDm">DM</xsl:when>
            <xsl:when test="self::reqCondPm">PM</xsl:when>
            <xsl:otherwise>GEN</xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block><xsl:value-of select="reqCond"/></fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:choose>
            <xsl:when test="dmRef">
              <xsl:call-template name="dm-code-string">
                <xsl:with-param name="c" select="dmRef/dmRefIdent/dmCode"/>
              </xsl:call-template>
            </xsl:when>
            <xsl:when test="pmRef">
              <xsl:call-template name="pm-code-string">
                <xsl:with-param name="c" select="pmRef/pmRefIdent/pmCode"/>
              </xsl:call-template>
            </xsl:when>
            <xsl:otherwise>—</xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <!--
    <refs> — the reference list a data module may carry at the head of its
    content. Presented as a table so a reader can scan the codes.
  -->
  <xsl:template match="refs">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'References'"/>
    </xsl:call-template>
    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" space-after="2mm">
      <fo:table-column column-width="{$body-w * 0.45}mm"/>
      <fo:table-column column-width="{$body-w * 0.55}mm"/>
      <fo:table-header>
        <fo:table-row>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold">CODE</fo:block>
          </fo:table-cell>
          <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
            <fo:block font-weight="bold">TITLE</fo:block>
          </fo:table-cell>
        </fo:table-row>
      </fo:table-header>
      <fo:table-body>
        <xsl:apply-templates select="dmRef|pmRef|externalPubRef" mode="reflist"/>
      </fo:table-body>
    </fo:table>
  </xsl:template>

  <xsl:template match="dmRef|pmRef|externalPubRef" mode="reflist">
    <fo:table-row>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:choose>
            <xsl:when test="self::dmRef">
              <xsl:call-template name="dm-code-string">
                <xsl:with-param name="c" select="dmRefIdent/dmCode"/>
              </xsl:call-template>
            </xsl:when>
            <xsl:when test="self::pmRef">
              <xsl:call-template name="pm-code-string">
                <xsl:with-param name="c" select="pmRefIdent/pmCode"/>
              </xsl:call-template>
            </xsl:when>
            <xsl:otherwise>
              <xsl:value-of select="externalPubRefIdent/externalPubCode"/>
            </xsl:otherwise>
          </xsl:choose>
        </fo:block>
      </fo:table-cell>
      <fo:table-cell border="{$cell-rule}" padding="1.2mm">
        <fo:block>
          <xsl:value-of select="dmRefAddressItems/dmTitle/techName"/>
          <xsl:if test="dmRefAddressItems/dmTitle/infoName">
            <xsl:text> — </xsl:text>
            <xsl:value-of select="dmRefAddressItems/dmTitle/infoName"/>
          </xsl:if>
          <xsl:value-of select="pmRefAddressItems/pmTitle"/>
          <xsl:value-of select="externalPubRefAddressItems/externalPubTitle"/>
        </fo:block>
      </fo:table-cell>
    </fo:table-row>
  </xsl:template>

  <!-- ========================= generic fall-backs ======================= -->

  <!--
    Nothing in a CSDB object is allowed to vanish silently. Any element a
    per-type stylesheet does not claim is still printed: containers recurse,
    leaves print as a labelled line built from the element name.
  -->
  <xsl:template match="*" priority="-1">
    <xsl:choose>
      <xsl:when test="*">
        <fo:block><xsl:apply-templates/></fo:block>
      </xsl:when>
      <xsl:when test="normalize-space(.) != ''">
        <fo:block space-after="1.5mm">
          <fo:inline font-weight="bold">
            <xsl:call-template name="element-label"/>
            <xsl:text>: </xsl:text>
          </fo:inline>
          <xsl:apply-templates/>
        </fo:block>
      </xsl:when>
    </xsl:choose>
  </xsl:template>

  <!--
    A labelled key/value table over an element's attributes — the readable way to
    present the metadata-heavy objects (ICN metadata, dispatch notes, comments).
  -->
  <xsl:template name="attribute-table">
    <xsl:param name="node" select="."/>
    <xsl:if test="$node/@*">
      <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
                font-size="{$fs-small}pt" space-after="2.5mm">
        <fo:table-column column-width="{$body-w * 0.35}mm"/>
        <fo:table-column column-width="{$body-w * 0.65}mm"/>
        <fo:table-body>
          <xsl:for-each select="$node/@*">
            <fo:table-row>
              <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
                <fo:block font-weight="bold">
                  <xsl:call-template name="camel-to-words">
                    <xsl:with-param name="text" select="local-name()"/>
                  </xsl:call-template>
                </fo:block>
              </fo:table-cell>
              <fo:table-cell border="{$cell-rule}" padding="1.2mm">
                <fo:block><xsl:value-of select="."/></fo:block>
              </fo:table-cell>
            </fo:table-row>
          </xsl:for-each>
        </fo:table-body>
      </fo:table>
    </xsl:if>
  </xsl:template>

  <!-- A two-column row for a hand-built key/value table. -->
  <xsl:template name="kv-row">
    <xsl:param name="label"/>
    <xsl:param name="value"/>
    <xsl:if test="string-length(normalize-space($value)) &gt; 0">
      <fo:table-row>
        <fo:table-cell border="{$cell-rule}" padding="1.2mm" background-color="{$shade}">
          <fo:block font-weight="bold"><xsl:value-of select="$label"/></fo:block>
        </fo:table-cell>
        <fo:table-cell border="{$cell-rule}" padding="1.2mm">
          <fo:block><xsl:value-of select="$value"/></fo:block>
        </fo:table-cell>
      </fo:table-row>
    </xsl:if>
  </xsl:template>

  <!--
    Set an XPath one location step per line. The renderer never breaks inside a
    word, so a path printed as a single string overruns its table cell; splitting
    it on the step separator keeps it inside the column and reads naturally.
  -->
  <xsl:template name="path-lines">
    <xsl:param name="path"/>
    <xsl:param name="prefix" select="''"/>
    <xsl:choose>
      <xsl:when test="contains($path, '/')">
        <xsl:variable name="step" select="substring-before($path, '/')"/>
        <xsl:variable name="rest" select="substring-after($path, '/')"/>
        <xsl:choose>
          <!-- "//" yields an empty step; carry it over to the next one. -->
          <xsl:when test="$step = ''">
            <xsl:call-template name="path-lines">
              <xsl:with-param name="path" select="$rest"/>
              <xsl:with-param name="prefix" select="concat($prefix, '/')"/>
            </xsl:call-template>
          </xsl:when>
          <xsl:otherwise>
            <fo:block><xsl:value-of select="concat($prefix, $step, '/')"/></fo:block>
            <xsl:call-template name="path-lines">
              <xsl:with-param name="path" select="$rest"/>
            </xsl:call-template>
          </xsl:otherwise>
        </xsl:choose>
      </xsl:when>
      <xsl:when test="string-length(concat($prefix, $path)) &gt; 0">
        <fo:block><xsl:value-of select="concat($prefix, $path)"/></fo:block>
      </xsl:when>
    </xsl:choose>
  </xsl:template>

  <xsl:template name="element-label">
    <xsl:call-template name="camel-to-words">
      <xsl:with-param name="text" select="local-name()"/>
    </xsl:call-template>
  </xsl:template>

  <!-- "reasonForUpdate" -> "Reason for update". -->
  <xsl:template name="camel-to-words">
    <xsl:param name="text"/>
    <xsl:param name="first" select="1"/>
    <xsl:if test="string-length($text) &gt; 0">
      <xsl:variable name="c" select="substring($text, 1, 1)"/>
      <xsl:variable name="upper" select="translate($c, 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ')"/>
      <xsl:variable name="lower" select="translate($c, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz')"/>
      <xsl:choose>
        <xsl:when test="$first = 1">
          <xsl:value-of select="$upper"/>
        </xsl:when>
        <xsl:when test="$c = $upper and $c != $lower">
          <xsl:text> </xsl:text>
          <xsl:value-of select="$lower"/>
        </xsl:when>
        <xsl:otherwise>
          <xsl:value-of select="$c"/>
        </xsl:otherwise>
      </xsl:choose>
      <xsl:call-template name="camel-to-words">
        <xsl:with-param name="text" select="substring($text, 2)"/>
        <xsl:with-param name="first" select="0"/>
      </xsl:call-template>
    </xsl:if>
  </xsl:template>

  <!-- The identification sections are presented by the title block, never inline. -->
  <xsl:template match="identAndStatusSection|imfIdentAndStatusSection|updateIdentAndStatusSection"/>

  <!-- referencedApplicGroup is consumed by applicability-annotation. -->
  <xsl:template match="referencedApplicGroup"/>

</xsl:stylesheet>

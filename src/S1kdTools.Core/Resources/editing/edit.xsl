<?xml version="1.0" encoding="UTF-8"?>
<!--
  edit.xsl — the editing projection's entry point.

  One stylesheet for every CSDB object type, rather than one per schema the way
  the presentation stylesheets are arranged. A presentation stylesheet has to be
  chosen because a procedure and a parts catalogue are laid out differently; an
  editing projection does not, because the shapes it emits — a block with text, a
  block with children, a labelled field — are the same whatever schema produced
  them. So the server opens any object without first deciding what it is, and an
  object type nobody has written a template for still opens, through the
  fall-through in edit-common.xsl.

  Two sections come out of every object:

  * **ident** — the identification and status data, as labelled fields. Curated:
    the handful of things an author changes (the title, the issue, the dates, the
    responsible company) rather than every element in the address.
  * **content** — the content itself, projected by edit-common.xsl and by the
    per-schema templates below.
-->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">

  <xsl:include href="edit-common.xsl"/>

  <xsl:output method="xml" indent="no" omit-xml-declaration="yes" encoding="UTF-8"/>

  <!-- ==================================================================== -->
  <!-- the document                                                         -->
  <!-- ==================================================================== -->

  <xsl:template match="/">
    <editDocument>
      <xsl:attribute name="root"><xsl:value-of select="name(*)"/></xsl:attribute>
      <xsl:attribute name="schema"><xsl:call-template name="schema-name"/></xsl:attribute>
      <xsl:attribute name="objectType"><xsl:call-template name="object-type"/></xsl:attribute>
      <xsl:attribute name="code"><xsl:call-template name="object-code"/></xsl:attribute>
      <xsl:attribute name="title"><xsl:call-template name="object-title"/></xsl:attribute>
      <xsl:apply-templates select="*" mode="document"/>
    </editDocument>
  </xsl:template>

  <xsl:template match="*" mode="document">
    <xsl:call-template name="ident-section"/>
    <section key="content" label="Content">
      <blocks>
        <xsl:apply-templates select="content/* | pmEntry | frontMatter">
          <xsl:with-param name="level" select="0"/>
        </xsl:apply-templates>
      </blocks>
    </section>
  </xsl:template>

  <!-- ==================================================================== -->
  <!-- identification and status                                            -->
  <!-- ==================================================================== -->

  <!--
    The address, as the fields an author actually edits. Everything here is
    addressed the same way the content is — by the path of the element or
    attribute it came from — so changing the tech name from this section and
    changing a paragraph in the content go through one command engine.
  -->
  <xsl:template name="ident-section">
    <!--
      Scoped to the address, not to the object. A dmRef inside a paragraph carries
      a dmTitle/techName of its own, so a document-wide //dmTitle/techName finds
      the title of every module this one refers to as well as its own — and offers
      all of them as "Technical name" fields to type into.
    -->
    <xsl:variable name="ident"
                  select="identAndStatusSection | imfIdentAndStatusSection |
                          updateIdentAndStatusSection"/>
    <section key="ident" label="Identification and status">
      <blocks>
        <xsl:apply-templates select="$ident//dmTitle/techName | $ident//pmTitle" mode="meta">
          <xsl:with-param name="label" select="'Technical name'"/>
        </xsl:apply-templates>
        <xsl:apply-templates select="$ident//dmTitle/infoName" mode="meta">
          <xsl:with-param name="label" select="'Information name'"/>
        </xsl:apply-templates>
        <xsl:apply-templates select="$ident//dmTitle/infoNameVariant" mode="meta">
          <xsl:with-param name="label" select="'Information name variant'"/>
        </xsl:apply-templates>

        <xsl:apply-templates select="$ident//issueInfo/@issueNumber" mode="meta-attr">
          <xsl:with-param name="label" select="'Issue number'"/>
        </xsl:apply-templates>
        <xsl:apply-templates select="$ident//issueInfo/@inWork" mode="meta-attr">
          <xsl:with-param name="label" select="'In work'"/>
        </xsl:apply-templates>
        <xsl:apply-templates select="$ident//issueDate/@year" mode="meta-attr">
          <xsl:with-param name="label" select="'Issue year'"/>
        </xsl:apply-templates>
        <xsl:apply-templates select="$ident//issueDate/@month" mode="meta-attr">
          <xsl:with-param name="label" select="'Issue month'"/>
        </xsl:apply-templates>
        <xsl:apply-templates select="$ident//issueDate/@day" mode="meta-attr">
          <xsl:with-param name="label" select="'Issue day'"/>
        </xsl:apply-templates>

        <xsl:apply-templates select="$ident/dmStatus/@issueType | $ident/pmStatus/@issueType" mode="meta-attr">
          <xsl:with-param name="label" select="'Issue type'"/>
          <xsl:with-param name="options"
                          select="'new,changed,deleted,revised,rinstate-changed,rinstate-revised,rinstate-status,status'"/>
        </xsl:apply-templates>
        <xsl:apply-templates select="$ident//security/@securityClassification" mode="meta-attr">
          <xsl:with-param name="label" select="'Security classification'"/>
        </xsl:apply-templates>

        <xsl:apply-templates select="$ident//responsiblePartnerCompany/enterpriseName" mode="meta">
          <xsl:with-param name="label" select="'Responsible partner company'"/>
        </xsl:apply-templates>
        <xsl:apply-templates select="$ident//originator/enterpriseName" mode="meta">
          <xsl:with-param name="label" select="'Originator'"/>
        </xsl:apply-templates>

        <xsl:apply-templates select="$ident/dmStatus/applic/displayText/simplePara |
                                     $ident/pmStatus/applic/displayText/simplePara"
                             mode="meta">
          <xsl:with-param name="label" select="'Applicability'"/>
        </xsl:apply-templates>

        <xsl:apply-templates select="$ident//reasonForUpdate/simplePara" mode="meta">
          <xsl:with-param name="label" select="'Reason for update'"/>
        </xsl:apply-templates>
      </blocks>
    </section>
  </xsl:template>

  <!-- A metadata field whose value is the element's own text. -->
  <xsl:template match="*" mode="meta">
    <xsl:param name="label"/>
    <block element="{name()}" kind="metaField" level="0" editable="text" label="{$label}">
      <xsl:call-template name="path-attr"/>
      <xsl:call-template name="runs"/>
    </block>
  </xsl:template>

  <!--
    A metadata field whose value is an attribute. The block carries the path of
    the owning *element* plus the attribute name, rather than an attribute path,
    because that is what the command engine needs to set it — and because an
    attribute that is currently absent still has an element to be set on.
  -->
  <xsl:template match="@*" mode="meta-attr">
    <xsl:param name="label"/>
    <xsl:param name="options" select="''"/>
    <block kind="metaField" level="0" editable="attr" label="{$label}"
           attrName="{name()}" value="{.}">
      <xsl:attribute name="element"><xsl:value-of select="name(..)"/></xsl:attribute>
      <xsl:attribute name="path">
        <xsl:for-each select="parent::*">
          <xsl:call-template name="path"/>
        </xsl:for-each>
      </xsl:attribute>
      <xsl:if test="$options != ''">
        <xsl:attribute name="options"><xsl:value-of select="$options"/></xsl:attribute>
      </xsl:if>
    </block>
  </xsl:template>

  <!-- ==================================================================== -->
  <!-- per-schema content                                                   -->
  <!-- ==================================================================== -->

  <!--
    The content root of a data module carries no heading of its own. It is the
    whole of what the section already says is the content, and `mainProcedure`
    below it is the one that means "the steps" — printing "Procedure" twice, once
    around everything and once around the steps, is what made this worth an
    override rather than the camel-case fallback.
  -->
  <xsl:template match="procedure|description">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'group'"/>
      <xsl:with-param name="level" select="$level"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="faultIsolation|faultDescr|faultIsolationProcedure|isolationProcedure|
                       isolationMainProcedure|isolationStepQuestion|isolationStepAnswer|
                       correctiveProcedure|checkList|checkListInfo|crew|crewRefCard|crewDrill|
                       maintPlanning|process|sb|frontMatter">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'group'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="heading"><xsl:call-template name="element-heading"/></xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <!--
    A parts catalogue. The catalogue sequence number and the item under it are the
    two rows of a parts list, so they are emitted as requirement-shaped blocks —
    a row of labelled fields — rather than as a tree of one-child wrappers the
    author would have to open four deep to reach a part number.
  -->
  <xsl:template match="illustratedPartsCatalog">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'group'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="heading" select="'Illustrated parts catalogue'"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="catalogSeqNumber">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'group'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="heading">
        <xsl:text>Item </xsl:text>
        <xsl:value-of select="concat(@systemCode, '-', @subSystemCode, @subSubSystemCode,
                                     '-', @assyCode, '-', @figureNumber)"/>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="itemSeqNumber">
    <xsl:param name="level" select="0"/>
    <block element="itemSeqNumber" kind="partRow" level="{$level}" editable="none"
           label="{@itemSeqNumberValue}">
      <xsl:call-template name="path-attr"/>
      <xsl:call-template name="block-flags"/>
      <blocks>
        <xsl:apply-templates select=".//partNumber | .//manufacturerCode | .//descrForPart |
                                     .//quantityPerNextHigherAssy | .//partSegment/itemIdentData/*[self::name]">
          <xsl:with-param name="level" select="$level + 1"/>
        </xsl:apply-templates>
      </blocks>
    </block>
  </xsl:template>

  <xsl:template match="descrForPart|quantityPerNextHigherAssy">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="text-block">
      <xsl:with-param name="kind" select="'field'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="label"><xsl:call-template name="element-heading"/></xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <!--
    The referenced applicability group and the reference list are the object's
    machinery rather than its prose. Shown, because an author does look at them,
    but not opened out: they are edited through the applicability and reference
    tools, not by typing into a display text.
  -->
  <xsl:template match="referencedApplicGroup">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'group'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="heading" select="'Applicability definitions'"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="applic">
    <xsl:param name="level" select="0"/>
    <block element="applic" kind="applic" level="{$level}" editable="none"
           heading="{@id}">
      <xsl:call-template name="path-attr"/>
      <xsl:call-template name="block-flags"/>
      <blocks>
        <xsl:apply-templates select="displayText/simplePara">
          <xsl:with-param name="level" select="$level + 1"/>
        </xsl:apply-templates>
      </blocks>
    </block>
  </xsl:template>

  <xsl:template match="refs">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'group'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="heading" select="'References'"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="refs/dmRef|refs/pmRef|refs/externalPubRef|
                       reqCondDm/dmRef|reqCondPm/pmRef|reqTechInfo/dmRef">
    <xsl:param name="level" select="0"/>
    <block element="{name()}" kind="reference" level="{$level}" editable="none">
      <xsl:call-template name="path-attr"/>
      <xsl:call-template name="block-flags"/>
      <xsl:attribute name="heading">
        <xsl:choose>
          <xsl:when test="dmRefAddressItems/dmTitle">
            <xsl:value-of select="normalize-space(dmRefAddressItems/dmTitle/techName)"/>
            <xsl:if test="dmRefAddressItems/dmTitle/infoName">
              <xsl:text> — </xsl:text>
              <xsl:value-of select="normalize-space(dmRefAddressItems/dmTitle/infoName)"/>
            </xsl:if>
          </xsl:when>
          <xsl:otherwise><xsl:call-template name="dm-code"/></xsl:otherwise>
        </xsl:choose>
      </xsl:attribute>
      <xsl:attribute name="value"><xsl:call-template name="dm-code"/></xsl:attribute>
    </block>
  </xsl:template>

  <!-- The address is presented in the ident section; it is not content. -->
  <xsl:template match="identAndStatusSection|imfIdentAndStatusSection|updateIdentAndStatusSection"/>

  <!-- ==================================================================== -->
  <!-- document-level strings                                               -->
  <!-- ==================================================================== -->

  <xsl:template name="schema-name">
    <xsl:variable name="location"
                  select="string(/*/@*[local-name() = 'noNamespaceSchemaLocation'])"/>
    <xsl:choose>
      <xsl:when test="contains($location, '.xsd')">
        <xsl:call-template name="basename-before-xsd">
          <xsl:with-param name="path" select="$location"/>
        </xsl:call-template>
      </xsl:when>
      <xsl:otherwise><xsl:value-of select="name(/*)"/></xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template name="basename-before-xsd">
    <xsl:param name="path"/>
    <xsl:choose>
      <xsl:when test="contains($path, '/')">
        <xsl:call-template name="basename-before-xsd">
          <xsl:with-param name="path" select="substring-after($path, '/')"/>
        </xsl:call-template>
      </xsl:when>
      <xsl:otherwise><xsl:value-of select="substring-before($path, '.xsd')"/></xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template name="object-type">
    <xsl:choose>
      <xsl:when test="//procedure">Procedure</xsl:when>
      <xsl:when test="//description">Descriptive</xsl:when>
      <xsl:when test="//illustratedPartsCatalog">Illustrated parts data</xsl:when>
      <xsl:when test="//faultIsolation">Fault isolation</xsl:when>
      <xsl:when test="//crew">Crew</xsl:when>
      <xsl:when test="//maintPlanning">Maintenance planning</xsl:when>
      <xsl:when test="//checkList">Checklist</xsl:when>
      <xsl:when test="//process">Process</xsl:when>
      <xsl:when test="//sb">Service bulletin</xsl:when>
      <xsl:when test="//frontMatter">Front matter</xsl:when>
      <xsl:when test="/pm">Publication module</xsl:when>
      <xsl:otherwise><xsl:value-of select="name(/*)"/></xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template name="object-title">
    <xsl:choose>
      <xsl:when test="//dmTitle">
        <xsl:value-of select="normalize-space(//dmTitle/techName)"/>
        <xsl:if test="//dmTitle/infoName">
          <xsl:text> — </xsl:text>
          <xsl:value-of select="normalize-space(//dmTitle/infoName)"/>
        </xsl:if>
      </xsl:when>
      <xsl:when test="//pmTitle">
        <xsl:value-of select="normalize-space(//pmTitle)"/>
      </xsl:when>
    </xsl:choose>
  </xsl:template>

  <xsl:template name="object-code">
    <xsl:choose>
      <xsl:when test="//dmIdent/dmCode">
        <xsl:for-each select="//dmIdent/dmCode">
          <xsl:call-template name="dm-code"/>
        </xsl:for-each>
      </xsl:when>
      <xsl:when test="//pmIdent/pmCode">
        <xsl:for-each select="//pmIdent/pmCode">
          <xsl:value-of select="concat('PMC-', @modelIdentCode, '-', @pmIssuer,
                                       '-', @pmNumber, '-', @pmVolume)"/>
        </xsl:for-each>
      </xsl:when>
    </xsl:choose>
  </xsl:template>

  <!--
    The data module code as it is written on a page, from whichever element in
    scope carries the code attributes. Called both from the document header (with
    the dmCode as context) and from a dmRef run (with the dmRef as context), so it
    looks for the attributes on itself or on a descendant rather than being told
    where they are.
  -->
  <xsl:template name="dm-code">
    <xsl:for-each select="(descendant-or-self::dmCode)[1]">
      <xsl:value-of select="concat('DMC-', @modelIdentCode, '-', @systemDiffCode,
                                   '-', @systemCode, '-', @subSystemCode, @subSubSystemCode,
                                   '-', @assyCode, '-', @disassyCode, @disassyCodeVariant,
                                   '-', @infoCode, @infoCodeVariant, '-', @itemLocationCode)"/>
    </xsl:for-each>
  </xsl:template>

</xsl:stylesheet>

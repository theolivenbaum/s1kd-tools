<?xml version="1.0" encoding="UTF-8"?>
<!--
  edit-common.xsl — the shared half of the editing projection.

  The presentation stylesheets turn a CSDB object into XSL-FO: a picture of a
  page. This one turns the same object into an *editing structure* — the same
  reading of the content, but addressed rather than laid out. Every block it
  emits carries the XPath of the element it came from, so an edit made against
  the block can be applied to that element and to nothing else.

  Output is a plain, un-namespaced tree:

      <editDocument schema="…" code="…" title="…">
        <section key="content" label="Content">
          <block path="/dmodule[1]/content[1]/…" element="para" kind="para"
                 label="" level="1" editable="text">
            <runs><run text="…"/><run kind="element" element="dmRef" src="3" text="…"/></runs>
            <attrs><attr name="applicRefId" value="app-1000" label="Applicability"/></attrs>
            <blocks>…</blocks>
          </block>
        </section>
      </editDocument>

  Three rules hold everywhere in here:

  * **A path is generated the way `XmlUtils.XPathOf` generates one** — every step
    named and positionally predicated among same-named siblings. The two have to
    agree exactly, because the projection produces the path and the command
    engine resolves it.
  * **Inline content is emitted as runs, not as text.** A run is either editable
    text (optionally carrying one emphasis), or an atomic element — a dmRef, an
    internalRef, an acronym — that the editor shows as a chip and never lets the
    author retype. Each run that came from a child element records that element's
    position in `@src`, which is what lets the command engine put the original
    element back rather than a reconstruction of it.
  * **Anything with no template of its own still appears.** The fall-through at
    the bottom emits an uneditable block naming the element, so an object using a
    part of S1000D this projection has not been taught still opens, and the parts
    it does understand are still editable.
-->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">

  <xsl:output method="xml" indent="no" omit-xml-declaration="yes" encoding="UTF-8"/>

  <!--
    No xsl:strip-space. It would need the input as an XmlReader rather than as a
    loaded XmlDocument, and it buys nothing here: structural templates select
    child *elements*, and the run templates drop a text node whose
    normalize-space() is empty. Whitespace between elements therefore never
    reaches the output on its own account.
  -->

  <!-- ==================================================================== -->
  <!-- paths                                                                -->
  <!-- ==================================================================== -->

  <!--
    The XPath of the context element, positionally predicated at every step.

    Deliberately identical in shape to what `XmlUtils.XPathOf` produces in C#:
    the projection writes these and the command engine resolves them, so a
    disagreement between the two would not be a cosmetic difference — it would
    apply an edit to the wrong element.
  -->
  <xsl:template name="path">
    <xsl:for-each select="ancestor-or-self::*">
      <xsl:value-of select="concat('/', name(), '[',
                            1 + count(preceding-sibling::*[name() = name(current())]), ']')"/>
    </xsl:for-each>
  </xsl:template>

  <!-- The path attribute, for the (many) places that emit one for the context node. -->
  <xsl:template name="path-attr">
    <xsl:attribute name="path"><xsl:call-template name="path"/></xsl:attribute>
  </xsl:template>

  <!-- ==================================================================== -->
  <!-- inline runs                                                          -->
  <!-- ==================================================================== -->

  <!--
    The inline content of the context element, as runs.

    `@src` is the element's position among its parent's child *elements* — not
    among same-named siblings — because that is the coordinate the command engine
    uses to hand the original element back when the author's edit did not touch
    it.
  -->
  <xsl:template name="runs">
    <runs>
      <xsl:apply-templates mode="run"/>
    </runs>
  </xsl:template>

  <xsl:template match="text()" mode="run">
    <!--
      Normalised, but not stripped: an author's paragraph is source-wrapped over
      several lines and the newlines and indentation between words are not
      content. normalize-space() would also eat the single space that separates a
      word from a following dmRef chip, so the leading and trailing spaces are
      put back when the raw text had them.
    -->
    <xsl:variable name="normalized" select="normalize-space(.)"/>
    <xsl:if test="string-length($normalized) &gt; 0">
      <run>
        <xsl:attribute name="text">
          <xsl:if test="starts-with(., ' ') or starts-with(., '&#10;') or starts-with(., '&#9;')">
            <xsl:text> </xsl:text>
          </xsl:if>
          <xsl:value-of select="$normalized"/>
          <xsl:variable name="last" select="substring(., string-length(.))"/>
          <xsl:if test="$last = ' ' or $last = '&#10;' or $last = '&#9;'">
            <xsl:text> </xsl:text>
          </xsl:if>
        </xsl:attribute>
      </run>
    </xsl:if>
  </xsl:template>

  <!--
    Emphasis, subscript and superscript are styled text rather than chips: the
    author types into them. They keep their `@src` all the same, so the original
    element — and any attribute on it this projection does not model, such as
    `emphasisType` — is reused when the run is written back.
  -->
  <xsl:template match="emphasis" mode="run">
    <run src="{count(preceding-sibling::*) + 1}" element="emphasis" text="{normalize-space(.)}">
      <xsl:attribute name="style">
        <xsl:choose>
          <xsl:when test="@emphasisType = 'em02'">italic</xsl:when>
          <xsl:when test="@emphasisType = 'em03'">underline</xsl:when>
          <xsl:otherwise>bold</xsl:otherwise>
        </xsl:choose>
      </xsl:attribute>
    </run>
  </xsl:template>

  <xsl:template match="subScript" mode="run">
    <run src="{count(preceding-sibling::*) + 1}" element="subScript" style="subscript"
         text="{normalize-space(.)}"/>
  </xsl:template>

  <xsl:template match="superScript" mode="run">
    <run src="{count(preceding-sibling::*) + 1}" element="superScript" style="superscript"
         text="{normalize-space(.)}"/>
  </xsl:template>

  <xsl:template match="verbatimText" mode="run">
    <run src="{count(preceding-sibling::*) + 1}" element="verbatimText" style="code"
         text="{normalize-space(.)}"/>
  </xsl:template>

  <!--
    A reference is atomic. Its text is derived — a data module code, a title, an
    acronym expansion — and retyping it in prose would not change what it points
    at, so the editor shows a chip and the original element goes back untouched.
  -->
  <xsl:template match="dmRef" mode="run">
    <xsl:call-template name="atomic-run">
      <xsl:with-param name="kind" select="'dmRef'"/>
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="dmRefAddressItems/dmTitle">
            <xsl:value-of select="normalize-space(dmRefAddressItems/dmTitle/techName)"/>
            <xsl:if test="dmRefAddressItems/dmTitle/infoName">
              <xsl:text> — </xsl:text>
              <xsl:value-of select="normalize-space(dmRefAddressItems/dmTitle/infoName)"/>
            </xsl:if>
          </xsl:when>
          <xsl:otherwise>
            <xsl:call-template name="dm-code"/>
          </xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
      <xsl:with-param name="target"><xsl:call-template name="dm-code"/></xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="pmRef" mode="run">
    <xsl:call-template name="atomic-run">
      <xsl:with-param name="kind" select="'pmRef'"/>
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="pmRefAddressItems/pmTitle">
            <xsl:value-of select="normalize-space(pmRefAddressItems/pmTitle)"/>
          </xsl:when>
          <xsl:otherwise>Publication</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="externalPubRef" mode="run">
    <xsl:call-template name="atomic-run">
      <xsl:with-param name="kind" select="'externalPubRef'"/>
      <xsl:with-param name="text">
        <xsl:value-of select="normalize-space(externalPubRefAddressItems/externalPubTitle |
                                              externalPubRefIdent/externalPubCode)"/>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="internalRef" mode="run">
    <xsl:call-template name="atomic-run">
      <xsl:with-param name="kind" select="'internalRef'"/>
      <xsl:with-param name="text">
        <xsl:variable name="id" select="@internalRefId"/>
        <xsl:variable name="target" select="//*[@id = $id]"/>
        <xsl:choose>
          <xsl:when test="$target/title">
            <xsl:value-of select="normalize-space($target/title)"/>
          </xsl:when>
          <xsl:when test="normalize-space(.) != ''">
            <xsl:value-of select="normalize-space(.)"/>
          </xsl:when>
          <xsl:otherwise><xsl:value-of select="$id"/></xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
      <xsl:with-param name="target" select="string(@internalRefId)"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="acronym" mode="run">
    <xsl:call-template name="atomic-run">
      <xsl:with-param name="kind" select="'acronym'"/>
      <xsl:with-param name="text" select="normalize-space(acronymTerm)"/>
      <xsl:with-param name="target" select="normalize-space(acronymDefinition)"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="quantity" mode="run">
    <xsl:call-template name="atomic-run">
      <xsl:with-param name="kind" select="'quantity'"/>
      <xsl:with-param name="text">
        <xsl:value-of select="normalize-space(quantityGroup/quantityValue)"/>
        <xsl:if test="quantityGroup/quantityValue/@quantityUnitOfMeasure">
          <xsl:text> </xsl:text>
          <xsl:value-of select="quantityGroup/quantityValue/@quantityUnitOfMeasure"/>
        </xsl:if>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="warningRef|cautionRef" mode="run">
    <xsl:call-template name="atomic-run">
      <xsl:with-param name="kind" select="name()"/>
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="self::warningRef">WARNING</xsl:when>
          <xsl:otherwise>CAUTION</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
      <xsl:with-param name="target" select="string(@refId)"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="footnote" mode="run">
    <xsl:call-template name="atomic-run">
      <xsl:with-param name="kind" select="'footnote'"/>
      <xsl:with-param name="text" select="normalize-space(.)"/>
    </xsl:call-template>
  </xsl:template>

  <!-- Anything inline this projection has not been taught: shown as a chip
       naming the element, and put back verbatim. Losing it would be worse. -->
  <xsl:template match="*" mode="run" priority="-1">
    <xsl:call-template name="atomic-run">
      <xsl:with-param name="kind" select="name()"/>
      <xsl:with-param name="text">
        <xsl:choose>
          <xsl:when test="normalize-space(.) != ''"><xsl:value-of select="normalize-space(.)"/></xsl:when>
          <xsl:otherwise><xsl:value-of select="name()"/></xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template name="atomic-run">
    <xsl:param name="kind"/>
    <xsl:param name="text"/>
    <xsl:param name="target" select="''"/>
    <run kind="element" atomic="1" element="{name()}" refKind="{$kind}"
         src="{count(preceding-sibling::*) + 1}" text="{$text}">
      <xsl:if test="$target != ''">
        <xsl:attribute name="target"><xsl:value-of select="$target"/></xsl:attribute>
      </xsl:if>
    </run>
  </xsl:template>

  <!-- ==================================================================== -->
  <!-- blocks                                                               -->
  <!-- ==================================================================== -->

  <!--
    A block with editable inline content: the common case, and every element
    whose author-visible substance *is* its own text — a paragraph, a title, a
    requirement condition, an equipment name.
  -->
  <xsl:template name="text-block">
    <xsl:param name="kind"/>
    <xsl:param name="label" select="''"/>
    <xsl:param name="level" select="0"/>
    <xsl:param name="placeholder" select="''"/>
    <block element="{name()}" kind="{$kind}" level="{$level}" editable="text">
      <xsl:call-template name="path-attr"/>
      <xsl:if test="$label != ''">
        <xsl:attribute name="label"><xsl:value-of select="$label"/></xsl:attribute>
      </xsl:if>
      <xsl:if test="$placeholder != ''">
        <xsl:attribute name="placeholder"><xsl:value-of select="$placeholder"/></xsl:attribute>
      </xsl:if>
      <xsl:call-template name="block-flags"/>
      <xsl:call-template name="runs"/>
      <xsl:call-template name="attrs"/>
    </block>
  </xsl:template>

  <!--
    A block that owns other blocks rather than text: a warning, a step, a figure,
    a table. `$children` selects what goes inside; the default is every child
    element, which is what a container usually wants.
  -->
  <xsl:template name="container-block">
    <xsl:param name="kind"/>
    <xsl:param name="label" select="''"/>
    <xsl:param name="level" select="0"/>
    <xsl:param name="children" select="*"/>
    <xsl:param name="heading" select="''"/>
    <block element="{name()}" kind="{$kind}" level="{$level}" editable="none">
      <xsl:call-template name="path-attr"/>
      <xsl:if test="$label != ''">
        <xsl:attribute name="label"><xsl:value-of select="$label"/></xsl:attribute>
      </xsl:if>
      <xsl:if test="$heading != ''">
        <xsl:attribute name="heading"><xsl:value-of select="$heading"/></xsl:attribute>
      </xsl:if>
      <xsl:call-template name="block-flags"/>
      <xsl:call-template name="attrs"/>
      <blocks>
        <xsl:apply-templates select="$children">
          <xsl:with-param name="level" select="$level + 1"/>
        </xsl:apply-templates>
      </blocks>
    </block>
  </xsl:template>

  <!--
    Whether the block can be removed or reordered, which is a question about its
    parent rather than about it: a paragraph inside a step is one of many and can
    go, while the single `mainProcedure` of a procedure is structural. The rule
    used here is that a block is removable when the element repeats — either it
    already has a same-named sibling, or its parent is one of the containers whose
    children are a list by definition.
  -->
  <xsl:template name="block-flags">
    <xsl:variable name="repeatable"
                  select="preceding-sibling::*[name() = name(current())] |
                          following-sibling::*[name() = name(current())]"/>
    <xsl:variable name="inList"
                  select="parent::mainProcedure | parent::proceduralStep | parent::isolationStep |
                          parent::levelledPara | parent::description | parent::commonInfo |
                          parent::safetyRqmts | parent::reqCondGroup | parent::listItem |
                          parent::randomList | parent::sequentialList | parent::definitionList |
                          parent::supportEquipDescrGroup | parent::supplyDescrGroup |
                          parent::spareDescrGroup | parent::closeRqmts | parent::correctiveProcedure"/>
    <xsl:if test="count($repeatable) &gt; 0 or count($inList) &gt; 0">
      <xsl:attribute name="canDelete">1</xsl:attribute>
    </xsl:if>
    <xsl:if test="count($repeatable) &gt; 0">
      <xsl:attribute name="canMove">1</xsl:attribute>
    </xsl:if>
  </xsl:template>

  <!--
    The attributes an author has business changing, and only those. `id` is here
    because internalRefs point at it, `applicRefId` because it is how a step is
    made model-specific, and the change marks because they are how a revision is
    declared. Everything else on the element is left where it is.
  -->
  <xsl:template name="attrs">
    <xsl:if test="@id or @applicRefId or @changeType or @changeMark or @reasonForUpdateRefIds">
      <attrs>
        <xsl:if test="@id">
          <attr name="id" value="{@id}" label="Identifier" type="text"/>
        </xsl:if>
        <xsl:if test="@applicRefId">
          <attr name="applicRefId" value="{@applicRefId}" label="Applicability" type="applic"/>
        </xsl:if>
        <xsl:if test="@changeType">
          <attr name="changeType" value="{@changeType}" label="Change" type="choice"
                options="add,delete,modify"/>
        </xsl:if>
        <xsl:if test="@changeMark">
          <attr name="changeMark" value="{@changeMark}" label="Change mark" type="choice"
                options="0,1"/>
        </xsl:if>
      </attrs>
    </xsl:if>
  </xsl:template>

  <!-- ==================================================================== -->
  <!-- the content elements                                                 -->
  <!-- ==================================================================== -->

  <xsl:template match="para|simplePara|notePara|warningAndCautionPara|attentionListItemPara|
                       reqCond|listItemTerm|listItemDefinition|attentionSequentialListItemPara">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="text-block">
      <xsl:with-param name="kind" select="'para'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="placeholder" select="'Paragraph text'"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="title">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="text-block">
      <xsl:with-param name="kind" select="'title'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="placeholder" select="'Title'"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="levelledPara">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'section'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="label">
        <xsl:number level="multiple" count="levelledPara" format="1.1.1.1.1"/>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="warning|caution|note|attention">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="name()"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="heading">
        <xsl:choose>
          <xsl:when test="self::warning">WARNING</xsl:when>
          <xsl:when test="self::caution">CAUTION</xsl:when>
          <xsl:when test="self::attention">ATTENTION</xsl:when>
          <xsl:otherwise>NOTE</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="proceduralStep|isolationStep|crewDrillStep">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'step'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="label"><xsl:call-template name="step-number"/></xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <!-- The ATA numbering the presentation stylesheets use, so the number beside a
       step in the editor is the number it will carry on the page. -->
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
      <xsl:otherwise>
        <xsl:number count="proceduralStep|isolationStep|crewDrillStep" format="a"/>
        <xsl:text>)</xsl:text>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="randomList|sequentialList|definitionList">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'list'"/>
      <xsl:with-param name="level" select="$level"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="listItem|definitionListItem">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'listItem'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="label">
        <xsl:choose>
          <xsl:when test="parent::sequentialList">
            <xsl:number count="listItem" format="1."/>
          </xsl:when>
          <xsl:when test="parent::randomList">
            <xsl:text>•</xsl:text>
          </xsl:when>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="figure|foldout">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'figure'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="label">
        <xsl:text>Fig </xsl:text>
        <xsl:number level="any" count="figure|foldout" format="1"/>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="graphic|multimediaObject">
    <xsl:param name="level" select="0"/>
    <block element="{name()}" kind="graphic" level="{$level}" editable="none"
           heading="{@infoEntityIdent}">
      <xsl:call-template name="path-attr"/>
      <xsl:call-template name="block-flags"/>
      <attrs>
        <attr name="infoEntityIdent" value="{@infoEntityIdent}" label="ICN" type="text"/>
        <xsl:if test="@reproductionWidth">
          <attr name="reproductionWidth" value="{@reproductionWidth}" label="Width" type="text"/>
        </xsl:if>
      </attrs>
    </block>
  </xsl:template>

  <!-- ==================================================================== -->
  <!-- tables                                                               -->
  <!-- ==================================================================== -->

  <xsl:template match="table">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'table'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="label">
        <xsl:text>Table </xsl:text>
        <xsl:number level="any" count="table" format="1"/>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="tgroup|thead|tbody|tfoot">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind">
        <xsl:choose>
          <xsl:when test="self::thead">tableHead</xsl:when>
          <xsl:otherwise>tableBody</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="children" select="*[not(self::colspec)]"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="row">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'row'"/>
      <xsl:with-param name="level" select="$level"/>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="entry">
    <xsl:param name="level" select="0"/>
    <xsl:choose>
      <!-- An entry holding paragraphs is a container; one holding words is a cell
           the author types into directly. -->
      <xsl:when test="para|simplePara|randomList|sequentialList|figure|warning|caution|note">
        <xsl:call-template name="container-block">
          <xsl:with-param name="kind" select="'cell'"/>
          <xsl:with-param name="level" select="$level"/>
        </xsl:call-template>
      </xsl:when>
      <xsl:otherwise>
        <xsl:call-template name="text-block">
          <xsl:with-param name="kind" select="'cell'"/>
          <xsl:with-param name="level" select="$level"/>
          <xsl:with-param name="placeholder" select="'Cell'"/>
        </xsl:call-template>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="colspec"/>

  <!-- ==================================================================== -->
  <!-- job set-up information                                               -->
  <!-- ==================================================================== -->

  <xsl:template match="preliminaryRqmts|closeRqmts">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'group'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="heading">
        <xsl:choose>
          <xsl:when test="self::preliminaryRqmts">Job set-up information</xsl:when>
          <xsl:otherwise>Requirements after job completion</xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <!--
    The job set-up sub-groups. Their headings are the ones a maintenance manual
    prints, not the element names opened out: an author looking for the consumables
    is looking for "Consumables, materials and expendables", and "Req supplies" —
    which is what the camel-case fallback would produce — is the name of a tag.

    The intermediate `…DescrGroup` wrappers get no heading at all. They exist in
    the schema to hold a list; printing "Support equip descr group" above a list
    that already sits under "Support equipment" tells the author nothing and costs
    a line.
  -->
  <xsl:template match="reqCondGroup|reqSupportEquips|reqSupplies|reqSpares|reqPersons|reqSafety|
                       safetyRqmts|supportEquipDescrGroup|supplyDescrGroup|spareDescrGroup|
                       personnel|reqTechInfoGroup|commonInfo|mainProcedure|correctiveProcedure">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'group'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="heading">
        <xsl:choose>
          <xsl:when test="self::commonInfo">General information</xsl:when>
          <xsl:when test="self::reqCondGroup">Required conditions</xsl:when>
          <xsl:when test="self::reqSupportEquips">Support equipment</xsl:when>
          <xsl:when test="self::reqSupplies">Consumables, materials and expendables</xsl:when>
          <xsl:when test="self::reqSpares">Spares</xsl:when>
          <xsl:when test="self::reqPersons or self::personnel">Personnel</xsl:when>
          <xsl:when test="self::reqSafety">Safety conditions</xsl:when>
          <xsl:when test="self::reqTechInfoGroup">Technical information</xsl:when>
          <xsl:when test="self::mainProcedure">Procedure</xsl:when>
          <xsl:when test="self::correctiveProcedure">Corrective procedure</xsl:when>
          <xsl:when test="self::supportEquipDescrGroup or self::supplyDescrGroup or
                          self::spareDescrGroup or self::safetyRqmts"/>
          <xsl:otherwise><xsl:call-template name="element-heading"/></xsl:otherwise>
        </xsl:choose>
      </xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <xsl:template match="reqCondDm|reqCondPm|reqCondNoRef|reqCondExternalPub">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="container-block">
      <xsl:with-param name="kind" select="'requirement'"/>
      <xsl:with-param name="level" select="$level"/>
    </xsl:call-template>
  </xsl:template>

  <!--
    Equipment, consumables and spares are a fixed shape — a name, a part number
    and a quantity — so they are emitted as one block with three labelled fields
    rather than as a tree the author has to navigate. The fields are ordinary
    blocks; only their `kind` differs, which is what tells the editor to put them
    on one line.
  -->
  <xsl:template match="supportEquipDescr|supplyDescr|spareDescr|person">
    <xsl:param name="level" select="0"/>
    <block element="{name()}" kind="requirement" level="{$level}" editable="none">
      <xsl:call-template name="path-attr"/>
      <xsl:call-template name="block-flags"/>
      <xsl:call-template name="attrs"/>
      <blocks>
        <xsl:apply-templates select="name|shortName|reqQuantity|personCategory|personSkill|
                                     estimatedTime|identNumber/partAndSerialNumber/partNumber |
                                     identNumber/manufacturerCode">
          <xsl:with-param name="level" select="$level + 1"/>
        </xsl:apply-templates>
      </blocks>
    </block>
  </xsl:template>

  <xsl:template match="name|shortName|reqQuantity|partNumber|manufacturerCode|estimatedTime|
                       techName|infoName|acronymTerm|acronymDefinition">
    <xsl:param name="level" select="0"/>
    <xsl:call-template name="text-block">
      <xsl:with-param name="kind" select="'field'"/>
      <xsl:with-param name="level" select="$level"/>
      <xsl:with-param name="label"><xsl:call-template name="element-heading"/></xsl:with-param>
    </xsl:call-template>
  </xsl:template>

  <!-- ==================================================================== -->
  <!-- fall-through                                                         -->
  <!-- ==================================================================== -->

  <!--
    An element with no template of its own. Emitted rather than dropped: an object
    that uses a part of S1000D this projection has not been taught still opens and
    still shows what is in it, and the parts that *are* understood stay editable.
    Its children are projected too, so an unknown wrapper does not hide a
    paragraph.
  -->
  <xsl:template match="*" priority="-1">
    <xsl:param name="level" select="0"/>
    <xsl:choose>
      <xsl:when test="*">
        <xsl:call-template name="container-block">
          <xsl:with-param name="kind" select="'unknown'"/>
          <xsl:with-param name="level" select="$level"/>
          <xsl:with-param name="heading"><xsl:call-template name="element-heading"/></xsl:with-param>
        </xsl:call-template>
      </xsl:when>
      <xsl:when test="normalize-space(.) != ''">
        <xsl:call-template name="text-block">
          <xsl:with-param name="kind" select="'field'"/>
          <xsl:with-param name="level" select="$level"/>
          <xsl:with-param name="label"><xsl:call-template name="element-heading"/></xsl:with-param>
        </xsl:call-template>
      </xsl:when>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="text()"/>

  <!-- ==================================================================== -->
  <!-- labels                                                               -->
  <!-- ==================================================================== -->

  <!-- `reqSupportEquips` reads as "Req support equips" — the element name with
       its camel humps opened out and the first letter capitalised. Crude, and
       right often enough that a per-element table of prettier names would mostly
       repeat it; the templates above override it where it is not. -->
  <xsl:template name="element-heading">
    <xsl:variable name="raw">
      <xsl:call-template name="camel-to-words">
        <xsl:with-param name="text" select="name()"/>
      </xsl:call-template>
    </xsl:variable>
    <xsl:value-of select="concat(translate(substring($raw, 1, 1),
                                 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'),
                                 substring($raw, 2))"/>
  </xsl:template>

  <xsl:template name="camel-to-words">
    <xsl:param name="text"/>
    <xsl:param name="first" select="true()"/>
    <xsl:if test="string-length($text) &gt; 0">
      <xsl:variable name="char" select="substring($text, 1, 1)"/>
      <xsl:if test="not($first) and contains('ABCDEFGHIJKLMNOPQRSTUVWXYZ', $char)">
        <xsl:text> </xsl:text>
      </xsl:if>
      <xsl:value-of select="translate($char, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ',
                                             'abcdefghijklmnopqrstuvwxyz')"/>
      <xsl:call-template name="camel-to-words">
        <xsl:with-param name="text" select="substring($text, 2)"/>
        <xsl:with-param name="first" select="false()"/>
      </xsl:call-template>
    </xsl:if>
  </xsl:template>

</xsl:stylesheet>

<?xml version="1.0" encoding="UTF-8"?>
<!--
  comment.xsl — comment (comment.xsd).

  A comment is feedback raised against a CSDB object. It prints as a form: who
  raised it, against what, at what priority, then the comment text, then the
  space in which the response is recorded.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:fo="http://www.w3.org/1999/XSL/Format">

  <xsl:import href="common.xsl"/>

  <xsl:template name="document-body">
    <xsl:call-template name="comment-header"/>
    <xsl:apply-templates select="/comment/commentContent"/>
    <xsl:call-template name="comment-response"/>
  </xsl:template>

  <xsl:template name="comment-header">
    <xsl:variable name="items"
                  select="/comment/identAndStatusSection/commentAddress/commentAddressItems"/>
    <xsl:variable name="st" select="/comment/identAndStatusSection/commentStatus"/>

    <fo:table table-layout="fixed" width="{$body-w}mm" border-collapse="collapse"
              font-size="{$fs-small}pt" space-after="4mm">
      <fo:table-column column-width="{$body-w * 0.3}mm"/>
      <fo:table-column column-width="{$body-w * 0.7}mm"/>
      <fo:table-body>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Originator'"/>
          <xsl:with-param name="value"
                          select="$items/commentOriginator/dispatchAddress/enterprise/enterpriseName"/>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Location'"/>
          <xsl:with-param name="value">
            <xsl:value-of select="$items/commentOriginator/dispatchAddress/address/city"/>
            <xsl:if test="$items/commentOriginator/dispatchAddress/address/country">
              <xsl:text>, </xsl:text>
              <xsl:value-of select="$items/commentOriginator/dispatchAddress/address/country"/>
            </xsl:if>
          </xsl:with-param>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Priority'"/>
          <xsl:with-param name="value" select="$st/commentPriority/@commentPriorityCode"/>
        </xsl:call-template>
        <xsl:call-template name="kv-row">
          <xsl:with-param name="label" select="'Response required'"/>
          <xsl:with-param name="value" select="$st/commentResponse/@responseType"/>
        </xsl:call-template>
      </fo:table-body>
    </fo:table>

    <xsl:if test="$st/commentRefs/commentRef">
      <xsl:call-template name="section-heading">
        <xsl:with-param name="text" select="'Raised against'"/>
      </xsl:call-template>
      <xsl:for-each select="$st/commentRefs/commentRef">
        <fo:block start-indent="6mm" space-after="1.2mm">
          <xsl:if test="dmRef">
            <xsl:call-template name="dm-code-string">
              <xsl:with-param name="c" select="dmRef/dmRefIdent/dmCode"/>
            </xsl:call-template>
          </xsl:if>
          <xsl:if test="commentRefAddressItems/remarks">
            <xsl:text> — </xsl:text>
            <xsl:value-of select="commentRefAddressItems/remarks"/>
          </xsl:if>
        </fo:block>
      </xsl:for-each>
    </xsl:if>
  </xsl:template>

  <xsl:template match="commentContent">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Comment'"/>
    </xsl:call-template>
    <fo:block start-indent="6mm"><xsl:apply-templates/></fo:block>
  </xsl:template>

  <xsl:template name="comment-response">
    <xsl:call-template name="section-heading">
      <xsl:with-param name="text" select="'Response'"/>
    </xsl:call-template>
    <fo:block start-indent="6mm" border="{$cell-rule}" padding="2mm" height="30mm">
      <fo:block font-style="italic" color="#666666" font-size="{$fs-small}pt">
        To be completed by the responsible partner company.
      </fo:block>
      <fo:block space-before="18mm"> </fo:block>
    </fo:block>
  </xsl:template>

</xsl:stylesheet>

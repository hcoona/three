<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="1.0"
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:msb="http://schemas.microsoft.com/developer/msbuild/2003"
  exclude-result-prefixes="msb">
  <xsl:strip-space elements="*"/>
  <!-- Identity template copies nodes unchanged -->
  <xsl:template match="@* | node()">
    <xsl:copy>
      <xsl:apply-templates select="@* | node()"/>
    </xsl:copy>
  </xsl:template>

  <!-- Sort elements with Include attribute in ItemGroup without Label attribute -->
  <xsl:template match="msb:ItemGroup[not(@Label)]">
    <xsl:copy>
      <xsl:apply-templates select="@*"/>
      <!-- Sort all elements with Include attribute -->
      <xsl:apply-templates select="*[ @Include ]">
        <xsl:sort select="@Include"/>
      </xsl:apply-templates>
      <!-- Preserve the original order of elements without Include attribute -->
      <xsl:apply-templates select="*[ not(@Include) ]"/>
    </xsl:copy>
  </xsl:template>
</xsl:stylesheet>

package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static org.assertj.core.api.Assertions.*;

import java.util.*;
import org.junit.jupiter.api.Test;

class HtmlTests {
  @Test
  void untrustedTextAndAttributeValuesCannotBecomeMarkup() {
    String payload = "\"><script>alert('x')</script>&";
    String output = el("div", payload, el("input").attr("value", payload)).render();
    assertThat(output)
        .doesNotContain("<script>", "value=\"\">")
        .contains("&lt;script&gt;", "&quot;&gt;", "&amp;");
    assertThatThrownBy(() -> el("script><img")).isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(() -> el("div").attr("onclick", "alert(1)"))
        .isInstanceOf(IllegalArgumentException.class);
  }

  @Test
  void componentsPreserveNativeFormAndSvgSyntax() {
    assertThat(
            el(
                    "div",
                    List.of(el("input").flag("required", true).flag("checked", false)),
                    el("svg", el("polyline").attr("points", "0,0 1,1"))
                        .attr("viewBox", "0 0 10 10"))
                .render())
        .contains("required=\"required\"", "viewBox=\"0 0 10 10\"", "</polyline>", "</svg>")
        .doesNotContain("</input>", "checked=");
  }
}

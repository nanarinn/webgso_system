package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import my.webgso.ConfiguredDatasets;

final class QuickApiTestPage {
  private QuickApiTestPage() {}

  static Html.Element render(PageContext c) {
    var dataset =
        el("select").attr("name", "dataType").attr("id", "quick-dataset").flag("required", true);
    String selected = c.text("dataType").isEmpty() ? "Rolling 24 Hour" : c.text("dataType");
    for (String name : ConfiguredDatasets.ALL)
      dataset.add(
          option(
              name, name.equals("MMF") ? "MMF - Weather measurements" : "DDQ - " + name, selected));
    return el(
        "section",
        heading(
            "API testing",
            "Test API without saving",
            "Try an API URL without adding a plant or saving results.",
            action("Back to Plant Config", "/admin/lss-plants", false)),
        el(
                "article",
                c.post(
                    "/admin/api-quick-test",
                    "java-form",
                    el("label", "API endpoint URL").attr("for", "quick-url"),
                    input("apiUrl", "url", c.text("apiUrl"))
                        .attr("id", "quick-url")
                        .attr("maxlength", 2048)
                        .attr("placeholder", "https://example.com/api")
                        .flag("required", true),
                    el("label", "Dataset").attr("for", "quick-dataset"),
                    dataset,
                    el(
                        "p",
                        "Checks connectivity and data structure only. No plant, readings, history,"
                            + " CSV report or email alerts are created. HTTPS and configured host"
                            + " restrictions still apply."),
                    el(
                            "div",
                            button("Test API", true),
                            action("Clear", "/admin/api-quick-test", false))
                        .cls("java-actions")))
            .cls("panel"));
  }
}

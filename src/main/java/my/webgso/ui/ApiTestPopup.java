package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import my.webgso.ui.Html.Element;

final class ApiTestPopup {
  private ApiTestPopup() {}

  static Element render(PageContext c) {
    boolean hasResponse = c.model().containsKey("apiTestHttpStatus");
    var popup =
        el(
            "dialog",
            el("p", "API connection test · " + c.text("apiTestDataset")).cls("section-label"),
            el("h2", c.text("apiTestTitle")).attr("id", "api-test-title"),
            el("p", c.text("apiTestMessage")).attr("id", "api-test-message"),
            el(
                    "div",
                    metric(
                        "HTTP status", hasResponse ? c.text("apiTestHttpStatus") : "No response"),
                    metric(
                        hasResponse ? "Response time" : "Test duration",
                        c.text(hasResponse ? "apiTestResponseMillis" : "apiTestDurationMillis")
                            + " ms"),
                    metric("Response size", hasResponse ? c.text("apiTestBytes") + " bytes" : "—"))
                .cls("api-test-metrics"));
    if (hasResponse)
      popup.add(
          el("p", "Content type: " + c.text("apiTestContentType")).cls("api-test-type"),
          el(
                  "details",
                  el("summary", "Preview response"),
                  el(
                          "pre",
                          el(
                              "code",
                              c.text("apiTestPreview").isEmpty()
                                  ? "Empty response"
                                  : c.text("apiTestPreview")))
                      .attr("tabindex", "0"),
                  c.yes("apiTestPreviewTruncated")
                      ? el(
                              "p",
                              "Preview limited to 12,000 characters. The full response was used for"
                                  + " the test.")
                          .cls("api-test-type")
                      : null,
                  el("p", "Common JSON credential fields are hidden in this preview.")
                      .cls("api-test-type"))
              .cls("api-test-response"));
    return popup
        .add(
            el(
                    "form",
                    el("button", "OK")
                        .attr("type", "submit")
                        .cls("primary-action")
                        .flag("autofocus", true))
                .attr("method", "dialog"))
        .cls("api-test-popup")
        .flag("open", true)
        .attr("aria-labelledby", "api-test-title")
        .attr("aria-describedby", "api-test-message");
  }

  private static Element metric(String title, String value) {
    return el("div", el("span", title), el("strong", value));
  }
}

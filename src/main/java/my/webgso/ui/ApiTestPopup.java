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
            c.text("apiTestFailureReason").isBlank() ? null
                : el("div", el("strong", c.text("apiTestFailureReason")),
                    el("p", "What to check: " + c.text("apiTestFailureAction")))
                    .cls("api-failure-guide").attr("role", "status"),
            freshness(c),
            c.text("apiTestFreshnessWarning").isBlank()
                ? null
                : el(
                        "div",
                        el("strong", "Data freshness warning"),
                        el("p", c.text("apiTestFreshnessWarning")))
                    .cls("api-freshness-warning")
                    .attr("role", "status"),
            c.yes("apiTestAlreadyTested")
                ? null
                : el(
                        "div",
                        metric(
                            "HTTP status",
                            hasResponse ? c.text("apiTestHttpStatus") : "No response"),
                        metric(
                            hasResponse ? "Response time" : "Test duration",
                            c.text(hasResponse ? "apiTestResponseMillis" : "apiTestDurationMillis")
                                + " ms"),
                        metric(
                            "Response size", hasResponse ? c.text("apiTestBytes") + " bytes" : "—"))
                    .cls("api-test-metrics"));
    if (hasResponse)
      popup.add(
          el("p", "Content type: " + c.text("apiTestContentType")).cls("api-test-type"),
          el(
                  "details",
                  el("summary", "View raw response"),
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

  static Element freshness(PageContext c) {
    if (!(c.model().get("apiTestFreshness") instanceof my.webgso.ApiDataFreshness.Status status))
      return null;
    return el("section",
        el("div", el("strong", "MMF data freshness"),
            el("span", status.label()).cls("freshness-badge")),
        el("dl", el("dt", "Latest source measurement (MYT)"), el("dd", status.latest()),
            el("dt", "Data age at time of test"), el("dd", status.age())),
        el("p", "Fresh: 5 minutes old or less. Based on the latest source timestamp, not the time you clicked Test."))
        .cls("api-freshness-status " + (status.label().equals("Fresh") ? "is-fresh" : "is-stale"))
        .attr("aria-label", "MMF data freshness");
  }
}

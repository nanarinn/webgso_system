package my.webgso.ui;

import static my.webgso.ui.Html.el;

import jakarta.servlet.http.HttpServletRequest;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.time.*;
import java.time.format.DateTimeFormatter;
import java.time.temporal.TemporalAccessor;
import java.util.*;
import my.webgso.ui.Html.Element;
import org.springframework.security.web.csrf.CsrfToken;

/** Per-request data and safe form/link components shared by the Java pages. */
public record PageContext(Map<String, Object> model, HttpServletRequest request) {
  @SuppressWarnings("unchecked")
  public <T> T value(String key) {
    return (T) model.get(key);
  }

  public String text(String key) {
    return Objects.toString(model.get(key), "");
  }

  public boolean yes(String key) {
    return Boolean.TRUE.equals(model.get(key));
  }

  public long number(String key) {
    return ((Number) model.getOrDefault(key, 0)).longValue();
  }

  @SuppressWarnings("unchecked")
  public <T> List<T> list(String key) {
    return (List<T>) model.getOrDefault(key, List.of());
  }

  public boolean parameter(String name) {
    return request.getParameter(name) != null;
  }

  public Element post(String action, String classes, Object... children) {
    var form = el("form").attr("action", action).attr("method", "post").cls(classes);
    var csrf = (CsrfToken) request.getAttribute(CsrfToken.class.getName());
    if (csrf == null) csrf = (CsrfToken) request.getAttribute("_csrf");
    if (csrf == null) throw new IllegalStateException("CSRF token missing from form request");
    return form.add(input(csrf.getParameterName(), "hidden", csrf.getToken()), children);
  }

  public String pageUrl(long page, String... filters) {
    var params = new LinkedHashMap<String, Object>();
    for (String filter : filters) params.put(filter, model.getOrDefault(filter, ""));
    params.put("page", page);
    return url(request.getRequestURI(), params);
  }

  public static String url(String path, Map<String, ?> parameters) {
    var parts = new ArrayList<String>();
    parameters.forEach(
        (key, value) -> {
          if (value != null) parts.add(encode(key) + "=" + encode(value.toString()));
        });
    return path + (parts.isEmpty() ? "" : "?" + String.join("&", parts));
  }

  public static String encode(String value) {
    return URLEncoder.encode(value, StandardCharsets.UTF_8);
  }

  public static Element input(String name, String type, Object value) {
    return el("input").attr("name", name).attr("type", type).attr("value", value);
  }

  public static Element link(String title, String href) {
    return el("a", title).attr("href", href);
  }

  public static Element action(String title, String href, boolean primary) {
    return link(title, href).cls(primary ? "primary-action" : "secondary-action");
  }

  public static Element button(String title, boolean primary) {
    return el("button", title)
        .attr("type", "submit")
        .cls(primary ? "primary-action" : "secondary-action");
  }

  public static Element field(String label, Element control) {
    return el("label", label, control);
  }

  public static Element option(Object value, Object label, Object selected) {
    return el("option", label)
        .attr("value", value)
        .flag("selected", Objects.toString(value, "").equals(Objects.toString(selected, "")));
  }

  public static Element select(String name, Iterable<?> values, Object selected) {
    var control = el("select").attr("name", name);
    values.forEach(value -> control.add(option(value, value, selected)));
    return control;
  }

  public static Element row(Object... values) {
    var row = el("tr");
    for (Object value : values) row.add(el("td", value));
    return row;
  }

  public static Element table(List<String> headings, List<Element> rows, String empty) {
    var header = el("tr");
    headings.forEach(title -> header.add(el("th", title).attr("scope", "col")));
    if (rows.isEmpty() && empty != null)
      rows =
          List.of(
              el(
                  "tr",
                  el(
                          "td",
                          el(
                                  "div",
                                  el(
                                          "svg",
                                          el("path").attr("d", "M4 5h16v14H4z M4 13h5l2 3h2l2-3h5"))
                                      .attr("viewBox", "0 0 24 24")
                                      .attr("fill", "none")
                                      .attr("stroke", "currentColor")
                                      .attr("stroke-width", "1.6")
                                      .attr("aria-hidden", "true")
                                      .cls("table-empty-icon"),
                                  el("strong", "Nothing to display yet"),
                                  el("p", empty))
                              .cls("table-empty-state"))
                      .attr("colspan", headings.size())
                      .cls("java-empty")));
    return el("div", el("table", el("thead", header), el("tbody", rows)))
        .cls("java-table-wrap")
        .attr("data-columns", headings.size())
        .attr("data-id-column", !headings.isEmpty() && headings.getFirst().equals("ID"));
  }

  public static Element heading(String label, String title, String subtitle, Object... actions) {
    return el(
            "header",
            el(
                "div",
                el("p", label).cls("section-label"),
                el("h1", title),
                subtitle == null ? null : el("p", subtitle).cls("page-subtitle")),
            actions)
        .cls("page-heading");
  }

  public static String date(TemporalAccessor value, String pattern) {
    return value == null ? "" : DateTimeFormatter.ofPattern(pattern, Locale.ENGLISH).format(value);
  }

  public static String dateTime(Object value) {
    return value instanceof LocalDateTime time
        ? date(time, "yyyy-MM-dd'T'HH:mm")
        : Objects.toString(value, "");
  }
}

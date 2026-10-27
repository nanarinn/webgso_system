package my.webgso.ui;

import java.util.*;
import org.springframework.web.util.HtmlUtils;

/** Small Java component tree. Text and attribute values are always escaped; no raw markup API. */
public final class Html {
  private Html() {}

  private static final Set<String> VOID = Set.of("meta", "link", "input", "img", "br", "hr");

  public static Element el(String tag, Object... children) {
    return new Element(tag).add(children);
  }

  public static final class Element {
    private final String tag;
    private final Map<String, String> attributes = new LinkedHashMap<>();
    private final List<Object> children = new ArrayList<>();

    private Element(String tag) {
      if (!tag.matches("[a-z][a-z0-9]*")) throw new IllegalArgumentException("Invalid tag");
      this.tag = tag;
    }

    public Element attr(String name, Object value) {
      if (!name.matches("[a-zA-Z][a-zA-Z0-9:-]*") || name.toLowerCase(Locale.ROOT).startsWith("on"))
        throw new IllegalArgumentException("Invalid attribute");
      if (value != null) attributes.put(name, value.toString());
      return this;
    }

    public Element flag(String name, boolean enabled) {
      if (enabled) attr(name, name);
      return this;
    }

    public Element cls(String value) {
      return attr("class", value);
    }

    public Element add(Object... content) {
      for (Object item : content) {
        if (item instanceof Iterable<?> items) items.forEach(i -> add(i));
        else if (item instanceof Object[] items) add(items);
        else if (item != null) children.add(item);
      }
      return this;
    }

    public String render() {
      var out = new StringBuilder();
      append(out);
      return out.toString();
    }

    private void append(StringBuilder out) {
      out.append('<').append(tag);
      attributes.forEach(
          (key, value) ->
              out.append(' ')
                  .append(key)
                  .append("=\"")
                  .append(HtmlUtils.htmlEscape(value))
                  .append('"'));
      out.append('>');
      if (VOID.contains(tag)) return;
      for (Object child : children) {
        if (child instanceof Element element) element.append(out);
        else out.append(HtmlUtils.htmlEscape(child.toString()));
      }
      out.append("</").append(tag).append('>');
    }
  }
}

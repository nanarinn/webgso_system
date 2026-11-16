package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;
import java.util.*;
import my.webgso.ConfiguredDatasets;
import my.webgso.Models.LssPlant;
import my.webgso.Models.PlantApiConfiguration;

final class LssDirectoryPage {
  static Html.Element render(PageContext c) {
    var plants = c.<LssPlant>list("plants");
    var ddq = new ArrayList<LssPlant>();
    var mmf = new ArrayList<LssPlant>();
    var unconfigured = new ArrayList<LssPlant>();
    var links = c.<PlantApiConfiguration>list("apiLinks");
    for (LssPlant plant : plants) {
      var datasets = ConfiguredDatasets.forPlant(plant, links);
      if (datasets.stream().anyMatch(type -> !type.equals("MMF"))) ddq.add(plant);
      if (datasets.contains("MMF")) mmf.add(plant);
      if (datasets.isEmpty()) unconfigured.add(plant);
    }
    var actions = el("div").cls("java-actions");
    if (c.yes("isAdmin")) {
      actions.add(action("Plant Config", "/admin/lss-plants", false),
          action("Add plant", "/admin/lss-plants/edit", true));
    } else {
      actions.add(action("Plant Config", "/user/plant-config", false));
    }
    var page = el("section",
        heading("Plant directory", "LSS plants", "Registered solar plants and their site information.",
            actions),
        group(c, "DDQ plants", "Plants with configured solar forecast APIs.", ddq),
        group(c, "MMF plants", "Plants with configured weather measurement APIs.", mmf));
    if (!unconfigured.isEmpty())
      page.add(group(c, "Plants without APIs", "Configure a DDQ or MMF API in Plant Config to categorise these plants.", unconfigured));
    return page.add(el("p", "Plant details are shared with Plant Config. Plants with both API types appear in both tables.").cls("muted"));
  }

  private static Html.Element group(PageContext c, String title, String description, List<LssPlant> plants) {
    var rows = new ArrayList<Html.Element>();
    for (var plant : plants)
      rows.add(row(plant.plantId, plant.name, plant.state,
          plant.capacityMw, Boolean.TRUE.equals(plant.isActive) ? "Active" : "Inactive",
          c.yes("isAdmin") ? action("View / Edit", "/admin/lss-plants/edit?id=" + plant.id, false) : el("span", "Read only")));
    return el("article",
        el("header", el("h2", title), el("p", description), el("p", plants.size() + " plant(s)")),
        table(List.of("Plant ID", "Plant name", "State", "Capacity (MW)", "Status", "Details"),
            rows, "No plants in this category.")).cls("panel");
  }
}

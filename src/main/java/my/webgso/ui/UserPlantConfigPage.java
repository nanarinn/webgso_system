package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;
import java.util.*;
import my.webgso.ConfiguredDatasets;
import my.webgso.Models.*;

/** User access to configured plants and individual API tests, without management actions. */
final class UserPlantConfigPage {
  static Html.Element render(PageContext c) {
    var rows = new ArrayList<Html.Element>();
    var links = c.<PlantApiConfiguration>list("apiLinks");
    for (var plant : c.<LssPlant>list("plants")) {
      var apis = el("div").cls("plant-table-api-list");
      for (String dataset : ConfiguredDatasets.forPlant(plant, links)) {
        String family = dataset.equals("MMF") ? "MMF" : "DDQ";
        String url = links.stream()
            .filter(link -> link.plantId == plant.id && dataset.equals(link.dataType)
                && family.equals(link.apiType) && link.apiLink != null && !link.apiLink.isBlank())
            .map(link -> link.apiLink).findFirst()
            .orElse(dataset.equals("MMF") ? plant.mmfApiEndpoint : plant.ddqApiEndpoint);
        apis.add(el("div", el("strong", dataset), el("p", url),
            c.post(family.equals("MMF") ? "/user/mmf-test" : "/user/ddq-test",
                "plant-table-api-test", input("apiUrl", "hidden", url),
                input("dataType", "hidden", dataset), button("Test API", false))));
      }
      if (ConfiguredDatasets.forPlant(plant, links).isEmpty()) apis.add(el("span", "Not configured"));
      rows.add(row(plant.id, plant.plantId, plant.name, plant.state, plant.capacityMw, apis));
    }
    return el("section", heading("Workspace", "Plant Config",
        "View registered plants and test each configured API. Contact an Admin to change plant or API settings."),
        el("article", table(List.of("ID", "Plant ID", "Plant name", "State", "Capacity (MW)", "API"),
            rows, "No plants registered.")).cls("panel"));
  }
}

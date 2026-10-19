// Shows which build (version) and which environment this page is served from.
//
// version.json -> baked into the IMAGE at build time (a fact about the box).
// env.json     -> mounted from a ConfigMap at RUNTIME (a fact about where the
//                 box is shelved). Never bake the environment into the image,
//                 or you need one image per environment and build-once breaks.
//
// Injected into Unity's index.html by the Dockerfile, so a fresh Unity export
// never wipes it.
(async function () {
  async function load(path) {
    try {
      const res = await fetch(path, { cache: "no-store" });
      return res.ok ? await res.json() : {};
    } catch (e) {
      return {};
    }
  }

  const [build, runtime] = await Promise.all([
    load("version.json"),
    load("env.json"),
  ]);
  const version = build.version || "unknown";
  const env = runtime.environment || "unknown";

  const colours = { dev: "#2e7d32", uat: "#ef6c00", prod: "#555" };
  const badge = document.createElement("div");
  badge.id = "build-info";
  badge.textContent =
    env === "prod" ? version : env.toUpperCase() + " · " + version;
  badge.style.cssText = [
    "position:fixed", "top:8px", "right:8px", "z-index:1000",
    "padding:4px 10px", "border-radius:4px",
    "font:12px/1.4 monospace", "color:#fff",
    "background:" + (colours[env] || "#b71c1c"),
    "opacity:" + (env === "prod" ? "0.5" : "0.9"),
    "pointer-events:none",
  ].join(";");
  document.body.appendChild(badge);
})();

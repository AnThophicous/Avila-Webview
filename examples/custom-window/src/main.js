let mica = true;
let rounded = true;

document.querySelector("#mica").addEventListener("click", async () => {
  mica = !mica;
  await avila.window.setMica(mica);
});

document.querySelector("#rounded").addEventListener("click", async () => {
  rounded = !rounded;
  await avila.window.setRoundedCorners(rounded);
});

document.querySelector("#center").addEventListener("click", () => avila.window.center());

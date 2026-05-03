const output = document.querySelector("#output");
const ping = document.querySelector("#ping");

async function render() {
  const info = await avila.app.info();
  const pong = await avila.system.ping();
  await avila.window.setMica(true);
  await avila.window.setRoundedCorners(true);
  output.textContent = JSON.stringify({ info, pong }, null, 2);
}

ping.addEventListener("click", async () => {
  const pong = await avila.system.ping();
  output.textContent = JSON.stringify(pong, null, 2);
});

render().catch(error => {
  output.textContent = error.message;
});

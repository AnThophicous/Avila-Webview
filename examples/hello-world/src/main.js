const output = document.querySelector("#output");
const copy = document.querySelector("#copy");

const info = await avila.app.info();
const pong = await avila.system.ping();
await avila.window.setTitle(`${info.name} - Avila`);
await avila.window.setMica(true);
await avila.window.setRoundedCorners(true);

output.textContent = JSON.stringify({ info, pong }, null, 2);

copy.addEventListener("click", () => avila.clipboard.writeText(output.textContent));

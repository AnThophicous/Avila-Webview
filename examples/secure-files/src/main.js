const output = document.querySelector("#output");
const filePath = "assets/sandbox-note.txt";

document.querySelector("#write").addEventListener("click", async () => {
  await avila.fs.writeFile(filePath, `Saved at ${new Date().toISOString()}`);
  output.textContent = "File written inside the project sandbox.";
});

document.querySelector("#read").addEventListener("click", async () => {
  const result = await avila.fs.readFile(filePath);
  output.textContent = result.content;
});

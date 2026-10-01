const rows = document.querySelector("[data-line-items]");
const add = document.querySelector("[data-add-line-item]");

// The button ships hidden so that without scripts there is no control that does nothing.
add.hidden = false;
add.addEventListener("click", () => {
  const index = rows.children.length;
  const row = rows.lastElementChild.cloneNode(true);
  for (const input of row.querySelectorAll("input")) {
    input.name = input.name.replace(/\[\d+\]/, `[${index}]`);
    input.id = input.id.replace(/_\d+__/, `_${index}__`);
    input.setAttribute("aria-label", input.getAttribute("aria-label").replace(/\d+/, index + 1));
    input.value = "";
  }
  rows.append(row);
  row.querySelector("input").focus();
});

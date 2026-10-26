const search = document.getElementById("claim-search");
if (search) {
    const rows = Array.from(document.querySelectorAll("#claims-table tbody tr"));
    const count = document.getElementById("claim-count");
    search.addEventListener("input", () => {
        const query = search.value.trim().toLocaleLowerCase();
        let visible = 0;
        for (const row of rows) {
            row.hidden = !row.textContent.toLocaleLowerCase().includes(query);
            if (!row.hidden) visible++;
        }
        count.textContent = `${visible} of ${rows.length} claim entries`;
    });
}

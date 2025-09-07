let currentPath = "/";

export function init(dotnetRef, targetPath) {
    currentPath = targetPath;
    let counter = 0;

    async function uploadPayload(files, dirs) {
        const formData = new FormData();

        for (const f of files) {
            const relPath = f.relativePath || f.webkitRelativePath || f.name;
            // Viktigt: navngiv med relativ sti, så serveren kan genskabe mappestruktur
            formData.append("files", f, relPath);
        }

        // Send hver mappe separat, så serveren kan oprette tomme mapper
        for (const d of dirs) {
            formData.append("dirs", d);
        }

        if (currentPath) formData.append("path", currentPath);

        const response = await fetch("/files/Upload", {
            method: "POST",
            body: formData
        });

        if (response.ok) {
            dotnetRef.invokeMethodAsync('OnUploadSuccess');
        }
    }

    // Læs alle entries fra en DirectoryReader (kræver flere kald)
    function readAllEntries(dirReader) {
        return new Promise((resolve, reject) => {
            const entries = [];
            const readBatch = () => {
                dirReader.readEntries(batch => {
                    if (batch.length === 0) resolve(entries);
                    else {
                        entries.push(...batch);
                        readBatch();
                    }
                }, reject);
            };
            readBatch();
        });
    }

    // Traverserer file system entries og samler filer + mapper
    function collectFromEntry(entry, basePath, files, dirs) {
        if (entry.isFile) {
            return new Promise((resolve, reject) => {
                entry.file(file => {
                    file.relativePath = basePath + file.name;
                    files.push(file);
                    resolve();
                }, reject);
            });
        } else if (entry.isDirectory) {
            const dirPath = basePath + entry.name + "/";
            dirs.add(dirPath); // registrér mappen (så også tomme oprettes)
            const reader = entry.createReader();
            return readAllEntries(reader).then(children =>
                Promise.all(children.map(child => collectFromEntry(child, dirPath, files, dirs)))
            );
        } else {
            return Promise.resolve();
        }
    }

    async function onDrop(e) {
        e.preventDefault();
        counter = 0;
        dotnetRef.invokeMethodAsync('Hide');

        const dt = e.dataTransfer;
        if (!dt) return;

        const items = dt.items;
        const files = [];
        const dirs = new Set();

        if (items && items.length > 0) {
            const tasks = [];
            for (let i = 0; i < items.length; i++) {
                const item = items[i];
                const entry = item.webkitGetAsEntry ? item.webkitGetAsEntry() : null;
                if (entry) {
                    // Hvis en mappe droppes, vil collectFromEntry sørge for at inkludere rodfolderens navn
                    tasks.push(collectFromEntry(entry, "", files, dirs));
                } else if (item.kind === "file") {
                    // Fallback for browsere/tilfælde uden entries: ingen rodfolder, kun filnavn
                    const file = item.getAsFile();
                    if (file) {
                        file.relativePath = file.name;
                        files.push(file);
                    }
                }
            }
            await Promise.all(tasks);
            if (files.length > 0 || dirs.size > 0) {
                await uploadPayload(files, dirs);
            }
        } else if (dt.files && dt.files.length > 0) {
            // Ren fil-drop (uden entries)
            for (const f of dt.files) {
                f.relativePath = f.webkitRelativePath || f.name;
            }
            await uploadPayload(Array.from(dt.files), new Set());
        }
    }

    function onDragEnter(e) {
        e.preventDefault();
        counter++;
        dotnetRef.invokeMethodAsync('Show');
    }

    function onDragLeave(e) {
        e.preventDefault();
        counter--;
        if (counter <= 0) {
            dotnetRef.invokeMethodAsync('Hide');
        }
    }

    function onDragOver(e) {
        e.preventDefault();
    }

    document.addEventListener('dragenter', onDragEnter);
    document.addEventListener('dragleave', onDragLeave);
    document.addEventListener('dragover', onDragOver);
    document.addEventListener('drop', onDrop);

    return {
        dispose: () => {
            document.removeEventListener('dragenter', onDragEnter);
            document.removeEventListener('dragleave', onDragLeave);
            document.removeEventListener('dragover', onDragOver);
            document.removeEventListener('drop', onDrop);
        }
    };
}

export function updatePath(newPath) {
    currentPath = newPath;
}
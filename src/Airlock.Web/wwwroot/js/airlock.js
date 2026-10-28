// Airlock's page script: the airlock.js contract of the U5 business logic model. It holds the staged files and every
// accepted upload's files for the page's lifetime, sends the submission, follows the terminal, copies and saves text,
// reports the browser's time zone and mirrors Blazor's reconnection state. It sends nothing but the submission.

const staged = new Map();
const uploads = new Map();
const minimumInterval = 250;
let nextId = 1;

// U5-NFR-07: Space selects a grid row without scrolling the page.
document.addEventListener("keydown", event => {
    if (event.key === " " && event.target instanceof Element && event.target.matches("[data-airlock-selectable]")) {
        event.preventDefault();
    }
});

// UR-07: the panel opens the file dialog by click, Enter or Space; picked files join the staged list, except one whose
// name and size match a staged file, and the input is cleared so the same file can be picked again.
export function bindArtifactPicker(panel, input, sink) {
    if (!panel || !input || panel.dataset.airlockBound) {
        return;
    }

    panel.dataset.airlockBound = "true";
    const open = () => input.click();
    panel.addEventListener("click", open);
    panel.addEventListener("keydown", event => {
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            open();
        }
    });
    input.addEventListener("change", () => {
        const added = [];
        for (const file of input.files) {
            if ([...staged.values()].some(held => held.name === file.name && held.size === file.size)) {
                continue;
            }

            const id = String(nextId++);
            staged.set(id, file);
            added.push({ id, name: file.name, size: file.size });
        }

        input.value = "";
        if (added.length > 0) {
            report(sink, "Staged", added);
        }
    });
}

export function unstage(id) {
    staged.delete(String(id));
}

// UR-10, U5-NFR-05: returns once the upload has started; progress and the answer go to the sink.
export function submitScan(target, sink) {
    const form = new FormData();
    let files = null;
    if (target.stagedIds) {
        files = target.stagedIds.map(id => staged.get(String(id))).filter(file => file !== undefined);
        for (const file of files) {
            form.append("artifacts", file, file.name);
        }
    } else {
        form.append("targetPath", target.targetPath ?? "");
    }

    send(form, files, sink);
}

// UR-33: the files the page uploaded for that scan, again; false when it no longer holds them.
export function resubmitScan(scanId, sink) {
    const files = uploads.get(scanId);
    if (!files) {
        return false;
    }

    const form = new FormData();
    for (const file of files) {
        form.append("artifacts", file, file.name);
    }

    send(form, files, sink);
    return true;
}

// UR-11: progress at most four times a second while bytes move, and once at the end; the answer as status, the scan's
// id, or the problem's title and detail; status 0 when no answer came.
function send(form, files, sink) {
    const request = new XMLHttpRequest();
    let lastReport = 0;
    let timer = 0;
    let latest = null;
    const flush = () => {
        clearTimeout(timer);
        timer = 0;
        if (latest) {
            report(sink, "Progress", latest.loaded, latest.total);
            latest = null;
            lastReport = Date.now();
        }
    };

    request.upload.addEventListener("progress", event => {
        latest = { loaded: event.loaded, total: event.lengthComputable ? event.total : 0 };
        const since = Date.now() - lastReport;
        if (since >= minimumInterval) {
            flush();
        } else if (!timer) {
            timer = setTimeout(flush, minimumInterval - since);
        }
    });
    request.upload.addEventListener("load", flush);
    request.addEventListener("load", () => {
        flush();
        const answer = parse(request);
        if (request.status === 202 && answer.scanId && files) {
            uploads.set(answer.scanId, files);
        }

        report(sink, "Completed", answer);
    });
    for (const failure of ["error", "abort", "timeout"]) {
        request.addEventListener(failure, () => {
            flush();
            report(sink, "Completed", { status: 0 });
        });
    }

    request.open("POST", new URL("api/v2/scans", document.baseURI));
    request.setRequestHeader("Accept", "application/json");
    request.send(form);
}

function parse(request) {
    let body = null;
    try {
        body = JSON.parse(request.responseText);
    } catch {
        // Not JSON: the status alone answers.
    }

    return {
        status: request.status,
        scanId: body?.id ?? null,
        title: body?.title ?? null,
        detail: body?.detail ?? null,
    };
}

// U5-NFR-05: if the circuit has gone, the call fails and the page simply stops reporting.
function report(sink, method, ...args) {
    sink.invokeMethodAsync(method, ...args).catch(() => { });
}

// UR-17: the view keeps to the newest line unless the analyst has scrolled up, and again once they are back at the end.
export function followTerminal(element) {
    if (!element || element.dataset.airlockFollowing) {
        return;
    }

    element.dataset.airlockFollowing = "true";
    let pinned = true;
    element.addEventListener("scroll", () => {
        pinned = element.scrollHeight - element.scrollTop - element.clientHeight < 8;
    });
    const toEnd = () => {
        if (pinned) {
            element.scrollTop = element.scrollHeight;
        }
    };
    new MutationObserver(toEnd).observe(element, { childList: true });
    toEnd();
}

// UR-31: the Clipboard API in a secure context; otherwise a hidden text area and the browser's copy command.
export async function copyText(text) {
    if (window.isSecureContext && navigator.clipboard) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // Refused: try the fallback.
        }
    }

    const area = document.createElement("textarea");
    area.value = text;
    area.setAttribute("readonly", "");
    area.className = "visually-hidden";
    document.body.appendChild(area);
    area.select();
    let copied = false;
    try {
        copied = document.execCommand("copy");
    } catch {
        copied = false;
    }

    area.remove();
    return copied;
}

// UR-19: saves the text under the name, as UTF-8 plain text.
export function downloadText(fileName, text) {
    const url = URL.createObjectURL(new Blob([text], { type: "text/plain;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

// UR-34: the browser's IANA time zone.
export function timeZone() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;
    } catch {
        return null;
    }
}

// UR-05, U5-NFR-06: Blazor puts its state classes on the connection bar and tells it of each change; the page's root
// carries the state for the pill's OFFLINE. Retry asks Blazor to reconnect, and Reload reloads.
export function watchConnection() {
    const bar = document.getElementById("components-reconnect-modal");
    if (!bar || bar.dataset.airlockWatching) {
        return;
    }

    bar.dataset.airlockWatching = "true";
    bar.addEventListener("components-reconnect-state-changed", event => {
        const state = event.detail?.state;
        const root = document.documentElement;
        if (!state || state === "hide") {
            root.removeAttribute("data-connection");
        } else {
            root.setAttribute("data-connection", state === "show" || state === "retrying" ? "reconnecting" : "failed");
        }
    });
    bar.querySelector("[data-airlock-retry]")?.addEventListener("click", async () => {
        try {
            if (!await Blazor.reconnect()) {
                location.reload();
            }
        } catch {
            // The server is still out of reach; the bar stays.
        }
    });
    bar.querySelector("[data-airlock-reload]")?.addEventListener("click", () => location.reload());
}

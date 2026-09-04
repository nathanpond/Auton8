import { toast } from "@/components/notifications/toast";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useSearchParams } from "react-router-dom";
import {
  Alert,
  Box,
  Button,
  Group,
  Loader,
  Modal,
  Stack,
  Text
} from "@mantine/core";
import { Dropzone } from "@mantine/dropzone";
import {
  Filemanager,
  getMenuOptions,
  Willow,
  WillowDark,
  type IApi,
  type IEntity,
  type IFileMenuOption,
  type IParsedEntity,
  type TContextMenuType
} from "@svar-ui/react-filemanager";
import { Locale } from "@svar-ui/react-core";
import "@svar-ui/react-filemanager/all.css";
import {
  copyDataStoreFile,
  copyDataStoreFolder,
  createDataStoreFolder,
  dataStoreFileDownloadUrl,
  deleteDataStoreFile,
  deleteDataStoreFolder,
  DataStoreListing,
  listDataStoreFiles,
  renameOrMoveDataStoreFile,
  renameOrMoveDataStoreFolder,
  uploadDataStoreFile
} from "@/api/datastores";
import CreateSqlDataStoreFromCsvModal from "./CreateSqlDataStoreFromCsvModal";

// SVAR uses path-strings as ids. Root is "/"; nested ids are "/a/b/c". The
// AutoNate backend already speaks the same dialect for folder paths, so we
// can pass them through with one wrinkle: files need their own composite id
// (folder + filename) since the backend's uuid is opaque to svar's tree.
function joinPath(folder: string, name: string): string {
  if (folder === "/" || folder === "") return `/${name}`;
  return `${folder}/${name}`;
}

function parentOfPath(p: string): string {
  const idx = p.lastIndexOf("/");
  if (idx <= 0) return "/";
  return p.slice(0, idx);
}

function nameOfPath(p: string): string {
  const parts = p.split("/").filter(Boolean);
  return parts[parts.length - 1] ?? "";
}

// Build the svar IEntity list for one folder's contents. We carry the
// real backend uuid via a `_fileId` extra field so download/delete can
// resolve it back without another round-trip.
function listingToEntities(listing: DataStoreListing): IEntity[] {
  const folders: IEntity[] = listing.folders.map((f) => ({
    id: f.folderPath,
    type: "folder",
    lazy: true
  }));
  const files: IEntity[] = listing.files.map((f) => ({
    id: joinPath(f.folderPath, f.filename),
    type: "file",
    size: f.sizeBytes,
    date: new Date(f.uploadedAtUtc),
    _fileId: f.id
  }));
  return [...folders, ...files];
}

function describeError(err: unknown, fallback: string): string {
  const reason = (err as { response?: { data?: { reason?: string } } })?.response?.data
    ?.reason;
  if (reason) return reason;
  if (err instanceof Error) return err.message;
  return fallback;
}

// Recursive walk of a webkitGetAsEntry() entry — handles both files and
// directories. Each file is tagged with a `.path` property (relative to
// the drop root) so the upload routine can rebuild the folder tree under
// the chosen target. Without this, react-dropzone's default file-selector
// path-tagging doesn't reliably fire on Finder folder drops in our build.
async function walkEntry(
  entry: FileSystemEntry,
  prefix: string,
  out: File[]
): Promise<void> {
  if (entry.isFile) {
    const fileEntry = entry as FileSystemFileEntry;
    const file = await new Promise<File>((resolve, reject) => {
      fileEntry.file(resolve, reject);
    });
    Object.defineProperty(file, "path", {
      value: prefix + entry.name,
      configurable: true
    });
    out.push(file);
    return;
  }
  if (entry.isDirectory) {
    const dirEntry = entry as FileSystemDirectoryEntry;
    const reader = dirEntry.createReader();
    // readEntries returns at most 100 items per call; loop until empty.
    const collected: FileSystemEntry[] = [];
    await new Promise<void>((resolve, reject) => {
      const read = () => {
        reader.readEntries((batch) => {
          if (batch.length === 0) {
            resolve();
            return;
          }
          collected.push(...batch);
          read();
        }, reject);
      };
      read();
    });
    for (const child of collected) {
      await walkEntry(child, prefix + entry.name + "/", out);
    }
  }
}

// Custom file aggregator for the Mantine/react-dropzone pipeline. Handles
// both drag-drop (DataTransferItemList with webkitGetAsEntry) and click-to-
// browse (FileList from the hidden input). For drag-drop of a folder, we
// recurse so the full directory tree ends up in the queue with each file's
// relative path tagged via `path`. The `event` arg is typed loosely because
// react-dropzone's DropEvent is a union of native + React synthetic events
// and we only read the shared dataTransfer / target shape.
async function getFilesWithFolderSupport(
  event: { dataTransfer?: DataTransfer | null; target?: EventTarget | null } | unknown
): Promise<File[]> {
  const out: File[] = [];
  const dt = (event as { dataTransfer?: DataTransfer | null }).dataTransfer;
  if (dt?.items?.length) {
    const items = Array.from(dt.items);
    for (const item of items) {
      const entry = item.webkitGetAsEntry?.();
      if (entry) {
        await walkEntry(entry, "", out);
      } else {
        const file = item.getAsFile();
        if (file) out.push(file);
      }
    }
    return out;
  }
  // Click-to-browse path: input.files. Preserve webkitRelativePath if set
  // (only fires when the input has the `webkitdirectory` attribute).
  const target = (event as { target?: EventTarget | null }).target as HTMLInputElement | null;
  const list = target?.files ?? dt?.files;
  if (list) {
    for (const file of Array.from(list)) {
      if (file.webkitRelativePath) {
        Object.defineProperty(file, "path", {
          value: file.webkitRelativePath,
          configurable: true
        });
      }
      out.push(file);
    }
  }
  return out;
}

function useColorScheme(): "light" | "dark" {
  const read = () =>
    document.documentElement.getAttribute("data-mantine-color-scheme") === "dark"
      ? "dark"
      : "light";
  const [scheme, setScheme] = useState<"light" | "dark">(read);
  useEffect(() => {
    const observer = new MutationObserver(() => setScheme(read()));
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["data-mantine-color-scheme"]
    });
    return () => observer.disconnect();
  }, []);
  return scheme;
}

export default function DataStoreFileManager({
  storeId,
  storeName
}: {
  storeId: string;
  storeName: string;
}) {
  const apiRef = useRef<IApi | null>(null);
  // Box wrapper ref scopes the toolbar querySelector so we don't latch onto
  // another datastore's file manager if one's mounted elsewhere on the page.
  const containerRef = useRef<HTMLDivElement | null>(null);
  // Slot we portal the upload icon into — svar's own toolbar right-cluster
  // that holds the eye/preview toggle and the cards/table/panels switcher.
  const [toolbarSlot, setToolbarSlot] = useState<Element | null>(null);

  // URL ↔ svar folder sync. Each folder navigation pushes a browser history
  // entry (?folder=/path); back/forward replays it. The skip flag breaks
  // the URL→svar→URL loop: when we re-issue set-path in response to a URL
  // change, the on('set-path') handler skips the resulting push.
  const [searchParams, setSearchParams] = useSearchParams();
  const skipNextSetPathPush = useRef(false);
  // setSearchParams is a stable callback across renders, but accessing it
  // from inside the init useMemo would close over the first render's copy.
  // The ref lets the api.on('set-path') handler always reach the current
  // setter without making init depend on it.
  const setSearchParamsRef = useRef(setSearchParams);
  useEffect(() => {
    setSearchParamsRef.current = setSearchParams;
  });
  const [rootData, setRootData] = useState<IEntity[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const scheme = useColorScheme();
  const Theme = scheme === "dark" ? WillowDark : Willow;

  // Upload-here dialog state. `target` is the destination folder path —
  // either the right-clicked folder (folder context menu) or the currently
  // viewed folder (body context menu). Decoupled from svar's built-in
  // upload component so we can target an arbitrary folder, not just the
  // active panel's current path.
  const [uploadTarget, setUploadTarget] = useState<string | null>(null);
  const [uploadFiles, setUploadFiles] = useState<File[]>([]);
  const [uploadBusy, setUploadBusy] = useState(false);

  const closeUploadModal = useCallback(() => {
    setUploadTarget(null);
    setUploadFiles([]);
  }, []);

  // CSV → SQL DataStore conversion modal target. Carries the in-source file
  // identity so the modal can fetch the bytes; the modal owns the rest of
  // the pipeline (create new SqlType store, preview, ingest, navigate).
  const [csvImportTarget, setCsvImportTarget] = useState<
    { fileId: string; filename: string } | null
  >(null);

  // The active panel's current path. Used when right-clicking dead space
  // (the "body" context menu has no entity to anchor onto).
  const getCurrentPath = useCallback((): string => {
    const api = apiRef.current;
    if (!api) return "/";
    const state = api.getState();
    const activePanel = (state.activePanel ?? 0) as 0 | 1;
    return state.panels?.[activePanel]?.path ?? "/";
  }, []);

  // Load the root folder once on mount; everything below it is fetched
  // lazily through the request-data event the FileManager fires when a
  // folder is opened. Re-mounting on storeId change is automatic via the
  // key prop we set at the call site.
  useEffect(() => {
    let alive = true;
    listDataStoreFiles(storeId, "/")
      .then((listing) => {
        if (!alive) return;
        setRootData(listingToEntities(listing));
      })
      .catch((err: unknown) => {
        if (!alive) return;
        setLoadError(describeError(err, "Failed to load files."));
      });
    return () => {
      alive = false;
    };
  }, [storeId]);

  const init = useMemo(
    () => (api: IApi) => {
      apiRef.current = api;

      // Fetch the children of a folder the user just expanded and push them
      // back into svar's in-memory tree. Errors are surfaced as toasts so
      // users see why a folder failed to load instead of just an empty pane.
      api.on("request-data", async (ev: { id: string }) => {
        try {
          const listing = await listDataStoreFiles(storeId, ev.id || "/");
          api.exec("provide-data", { id: ev.id, data: listingToEntities(listing) });
        } catch (err) {
          toast.error(describeError(err, "Failed to load folder."));
        }
      });

      // create-file fires for both "new folder" and "file upload". We POST
      // to the right endpoint and let svar's default tree update run after
      // the backend confirms; on failure we cancel by returning false so
      // the UI doesn't show a phantom entry.
      api.intercept(
        "create-file",
        async (ev: {
          file: { name: string; type?: "file" | "folder"; file?: File };
          parent: string;
          newId?: string;
        }) => {
          const parent = ev.parent || "/";
          const isFolder = ev.file.type === "folder" || !ev.file.file;
          try {
            if (isFolder) {
              const folderPath = joinPath(parent, ev.file.name);
              await createDataStoreFolder(storeId, folderPath);
              if (ev.newId && ev.newId !== folderPath) {
                api.exec("file-renamed", { id: ev.newId, newId: folderPath });
              }
            } else {
              const uploaded = await uploadDataStoreFile(
                storeId,
                parent,
                ev.file.file!
              );
              const realId = joinPath(uploaded.folderPath, uploaded.filename);
              if (ev.newId && ev.newId !== realId) {
                api.exec("file-renamed", { id: ev.newId, newId: realId });
              }
            }
          } catch (err) {
            toast.error(describeError(err, "Create failed."));
            return false;
          }
        }
      );

      // delete-files passes a list of ids that can mix files and folders.
      // Files have _fileId on the entity (mapped from the backend uuid);
      // folders use their path directly. Run them sequentially so one
      // failure doesn't leave the tree partially desynced.
      api.intercept("delete-files", async (ev: { ids: string[] }) => {
        try {
          for (const id of ev.ids) {
            const entity = api.getFile(id);
            if (!entity) continue;
            if (entity.type === "folder") {
              await deleteDataStoreFolder(storeId, id);
            } else {
              const fileId = (entity as { _fileId?: string })._fileId;
              if (fileId) await deleteDataStoreFile(storeId, fileId);
            }
          }
        } catch (err) {
          toast.error(describeError(err, "Delete failed."));
          return false;
        }
      });

      // Svar's default download-file path tries to fetch through its data
      // provider; we don't ship one, so we take over and hand the browser
      // a same-origin URL that streams from the API with the session cookie.
      api.intercept("download-file", (ev: { id: string }) => {
        const entity = api.getFile(ev.id);
        const fileId = (entity as { _fileId?: string } | null)?._fileId;
        if (!fileId) {
          toast.error("This item can't be downloaded.");
          return false;
        }
        const name = nameOfPath(ev.id);
        const a = document.createElement("a");
        a.href = dataStoreFileDownloadUrl(storeId, fileId);
        a.download = name;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        return false;
      });

      // After any folder-affecting mutation, svar's in-memory tree may
      // disagree with the new server state (especially for folder
      // rename/copy, where descendant ids change). Reload the affected
      // parent folders so the next render reflects the truth.
      const refreshFolders = async (folders: Iterable<string>) => {
        const seen = new Set<string>();
        for (const folder of folders) {
          const key = folder || "/";
          if (seen.has(key)) continue;
          seen.add(key);
          try {
            const listing = await listDataStoreFiles(storeId, key);
            api.exec("provide-data", { id: key, data: listingToEntities(listing) });
          } catch {
            // Swallow — the next user-initiated navigation will recover.
          }
        }
      };

      // Rename — same parent, new last segment. For a file we only change
      // the filename; for a folder we PATCH the folder path. svar's
      // default-action tree update is suppressed (return false) and we
      // reload the parent so the new id flows through any stale references.
      api.intercept(
        "rename-file",
        async (ev: { id: string; name: string }) => {
          const entity = api.getFile(ev.id);
          if (!entity) return false;
          const parent = parentOfPath(ev.id);
          try {
            if (entity.type === "folder") {
              await renameOrMoveDataStoreFolder(storeId, ev.id, joinPath(parent, ev.name));
            } else {
              const fileId = (entity as { _fileId?: string })._fileId;
              if (!fileId) return false;
              await renameOrMoveDataStoreFile(storeId, fileId, null, ev.name);
            }
          } catch (err) {
            toast.error(describeError(err, "Rename failed."));
            return false;
          }
          await refreshFolders([parent]);
          return false;
        }
      );

      // Move — many items, single target folder. Per-item we PATCH a file
      // (folder path only) or PATCH a folder (compute new path under
      // target). Bail on the first failure so the tree doesn't end up
      // half-moved.
      api.intercept(
        "move-files",
        async (ev: { ids: string[]; target: string }) => {
          const affectedParents = new Set<string>([ev.target || "/"]);
          try {
            for (const id of ev.ids) {
              const entity = api.getFile(id);
              if (!entity) continue;
              affectedParents.add(parentOfPath(id));
              if (entity.type === "folder") {
                const newPath = joinPath(ev.target || "/", nameOfPath(id));
                await renameOrMoveDataStoreFolder(storeId, id, newPath);
              } else {
                const fileId = (entity as { _fileId?: string })._fileId;
                if (!fileId) continue;
                await renameOrMoveDataStoreFile(storeId, fileId, ev.target || "/", null);
              }
            }
          } catch (err) {
            toast.error(describeError(err, "Move failed."));
            await refreshFolders(affectedParents);
            return false;
          }
          await refreshFolders(affectedParents);
          return false;
        }
      );

      // Copy — many items, single target folder. Source rows stay put;
      // the target gets fresh ids (and fresh byte copies for files).
      api.intercept(
        "copy-files",
        async (ev: { ids: string[]; target: string }) => {
          const target = ev.target || "/";
          try {
            for (const id of ev.ids) {
              const entity = api.getFile(id);
              if (!entity) continue;
              if (entity.type === "folder") {
                const newPath = joinPath(target, nameOfPath(id));
                await copyDataStoreFolder(storeId, id, newPath);
              } else {
                const fileId = (entity as { _fileId?: string })._fileId;
                if (!fileId) continue;
                await copyDataStoreFile(storeId, fileId, target, null);
              }
            }
          } catch (err) {
            toast.error(describeError(err, "Copy failed."));
            await refreshFolders([target]);
            return false;
          }
          await refreshFolders([target]);
          return false;
        }
      );

      // URL history sync — every folder navigation pushes a `?folder=…`
      // query param so the browser back button moves up a folder instead
      // of leaving the data store entirely. The skipNextSetPathPush flag
      // is set by the URL→svar effect right before it re-issues set-path,
      // and reset here on the next handler tick, so a navigation that
      // *originated* from a URL change doesn't push the URL back.
      api.on("set-path", (ev: { id: string }) => {
        if (skipNextSetPathPush.current) {
          skipNextSetPathPush.current = false;
          return;
        }
        const folder = ev.id || "/";
        setSearchParamsRef.current((prev) => {
          const next = new URLSearchParams(prev);
          if (folder === "/") next.delete("folder");
          else next.set("folder", folder);
          return next;
        });
      });
    },
    [storeId]
  );

  // Context-menu extension: adds an "Upload file" entry to both the dead-
  // space ("body") menu and the per-folder menu. We sidestep svar's built-in
  // comp:"upload" widget because that one always targets the active panel's
  // current path; we want the right-clicked folder to be the target instead.
  // IMenuOption.handler is invoked by @svar-ui/react-menu before the outer
  // onClick chain, so opening our modal here doesn't fight svar's own
  // performAction dispatcher (which has no "upload-here" case anyway and
  // therefore silently no-ops, which is what we want).
  const menuOptionsCallback = useCallback(
    (mode: TContextMenuType, item?: IParsedEntity): IFileMenuOption[] | false => {
      const defaults = getMenuOptions(mode) as IFileMenuOption[];
      if (mode === "body") {
        return [
          ...defaults,
          {
            id: "upload-here",
            // FontAwesome over MDI: svar's CSS doesn't bundle the MDI webfont
            // (the default Add-New menu's mdi-* icons render as missing
            // glyphs in this build). The app already ships FontAwesome.
            icon: "fa fa-file-arrow-up",
            text: "Upload file",
            handler: () => setUploadTarget(getCurrentPath())
          }
        ];
      }
      if (mode === "folder" && item) {
        // SVAR's resolver hardcodes `Y.filter(V => V.id === "paste")` when
        // the right-clicked folder is the root ("/"), so anything we add
        // with our own id gets stripped before display. Smuggle the upload
        // entry past that filter by labeling it id:"paste" only for root.
        //
        // Two known side effects, both deemed acceptable for v1:
        // 1. React logs "duplicate key 'paste'" in dev because SVAR's menu
        //    uses option.id as the React key. The warning is cosmetic —
        //    both items still render and click correctly, and production
        //    builds suppress it.
        // 2. Clicks on our entry also dispatch the paste action. That's a
        //    no-op when the clipboard is empty (the common case). If the
        //    user has previously cut/copied something AND chooses Upload
        //    from root, paste also fires alongside our modal — rare and
        //    recoverable (delete the unwanted pasted items).
        const isRoot = item.id === "/";
        return [
          ...defaults,
          {
            id: isRoot ? "paste" : "upload-here",
            // FontAwesome over MDI: svar's CSS doesn't bundle the MDI webfont
            // (the default Add-New menu's mdi-* icons render as missing
            // glyphs in this build). The app already ships FontAwesome.
            icon: "fa fa-file-arrow-up",
            text: "Upload file",
            handler: () => setUploadTarget(item.id)
          }
        ];
      }
      // File right-click — only CSVs get the "Create SQL DataStore..." extra,
      // since that's the only file type the SQL ingest pipeline understands.
      // _fileId is carried on the IEntity from listingToEntities and lets the
      // modal fetch the bytes via the regular file download endpoint.
      if (mode === "file" && item) {
        const filename = nameOfPath(item.id);
        if (!filename.toLowerCase().endsWith(".csv")) return defaults;
        const api = apiRef.current;
        const entity = api?.getFile(item.id);
        const fileId = (entity as { _fileId?: string } | null)?._fileId;
        if (!fileId) return defaults;
        return [
          ...defaults,
          {
            id: "create-sql-datastore",
            icon: "fa fa-database",
            text: "Create SQL DataStore...",
            handler: () => setCsvImportTarget({ fileId, filename })
          }
        ];
      }
      return defaults;
    },
    [getCurrentPath]
  );

  // Shared upload routine — used by both the modal's Upload button and the
  // capture-phase drop handler that hijacks folder drops on svar's file
  // manager. Walks files sequentially so a mid-batch failure doesn't leave
  // the queue in an unclear state.
  //
  // Folder structure handling: when react-dropzone (or our drop hijacker)
  // delivers files from a dropped folder, each File carries its relative
  // path via `.path` (set by us / by react-dropzone's file-selector) or
  // `webkitRelativePath` (browse-via-input on a webkitdirectory input).
  // We split that into `relDir` and route each file to `target + relDir`.
  // No explicit folder-create call is needed — the backend's prefix-scan
  // listing synthesizes parent folders from any file's folder_path, so a
  // single file at `/uploads/myproject/sub/x.txt` is enough to materialize
  // `myproject/` and `myproject/sub/` in the tree.
  const uploadFilesToTarget = useCallback(
    async (target: string, files: File[]): Promise<{ uploaded: number; failed: number }> => {
      let uploaded = 0;
      let failed = 0;
      for (const f of files) {
        const relPath =
          (f as { path?: string }).path ?? f.webkitRelativePath ?? "";
        const cleanRel = relPath.replace(/^\/+/, "");
        const lastSlash = cleanRel.lastIndexOf("/");
        const relDir = lastSlash > 0 ? cleanRel.slice(0, lastSlash) : "";
        const folder = relDir ? joinPath(target, relDir) : target;
        try {
          await uploadDataStoreFile(storeId, folder, f);
          uploaded += 1;
        } catch (err) {
          failed += 1;
          toast.error(`${describeError(err, "Upload failed.")} ${relPath || f.name}`);
        }
      }
      const api = apiRef.current;
      if (api && uploaded > 0) {
        try {
          const listing = await listDataStoreFiles(storeId, target);
          api.exec("provide-data", { id: target, data: listingToEntities(listing) });
        } catch {
          // Refresh failure is non-fatal — user can navigate away and back.
        }
      }
      return { uploaded, failed };
    },
    [storeId]
  );

  const runUpload = useCallback(async () => {
    if (uploadTarget === null || uploadFiles.length === 0) return;
    setUploadBusy(true);
    const target = uploadTarget;
    try {
      const result = await uploadFilesToTarget(target, uploadFiles);
      if (result.uploaded > 0) {
        toast.success(`Uploaded ${result.uploaded} file${result.uploaded === 1 ? "" : "s"} to ${target}.`);
      }
      if (result.failed === 0) closeUploadModal();
    } finally {
      setUploadBusy(false);
    }
  }, [uploadTarget, uploadFiles, uploadFilesToTarget, closeUploadModal]);

  // The FileTree's root entity has a hardcoded `name: "My files"`; both the
  // side tree and breadcrumb pass it through the filemanager locale group's
  // translation function ONLY when `id === "/"` (renderer special-case).
  // Overriding that one locale key retitles the root in both places at once,
  // without us having to reach into the in-memory tree. Must be declared
  // above any early-return so React's hook order is stable across renders.
  const localeWords = useMemo(
    () => ({ filemanager: { "My files": storeName } }),
    [storeName]
  );

  // Find the toolbar right-cluster (eye + mode switcher) once svar has
  // rendered the file manager DOM. A MutationObserver covers the race
  // where svar's first paint happens after our effect runs, plus any
  // narrow-mode reflow that re-creates the toolbar wrapper. Scoped to
  // the wrapping container so it can't latch onto a sibling instance.
  useEffect(() => {
    if (rootData === null) return;
    const container = containerRef.current;
    if (!container) return;
    const tryAttach = () => {
      const el = container.querySelector(".wx-toolbar .wx-right");
      if (el && el !== toolbarSlot) setToolbarSlot(el);
    };
    tryAttach();
    const observer = new MutationObserver(tryAttach);
    observer.observe(container, { childList: true, subtree: true });
    return () => observer.disconnect();
  }, [rootData, toolbarSlot]);

  // Capture-phase drop hijacker for folder drops on svar's file manager.
  // Svar's own uploader recurses into directory entries but throws away the
  // accumulated path prefix at the leaf — so each file ends up uploaded to
  // the current folder with just its base name, no nested structure. We
  // listen in the capture phase on our wrapping container, which runs
  // BEFORE svar's drop listener (capture phase descends parent→child). If
  // any directory is in the dropped items, we hijack the event entirely:
  // walk the entries ourselves with path tagging, fire stopImmediate-
  // Propagation so svar's listener never gets the event, and upload via
  // the shared routine. Pure file drops we leave alone so svar's existing
  // happy-path keeps working.
  useEffect(() => {
    if (rootData === null) return;
    const container = containerRef.current;
    if (!container) return;
    const handler = async (e: DragEvent) => {
      const dt = e.dataTransfer;
      if (!dt?.items?.length) return;
      const items = Array.from(dt.items);
      const entries = items
        .map((i) => i.webkitGetAsEntry?.() ?? null)
        .filter((entry): entry is FileSystemEntry => entry !== null);
      const hasDirectory = entries.some((entry) => entry.isDirectory);
      if (!hasDirectory) return;
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();
      const target = getCurrentPath();
      const files: File[] = [];
      try {
        for (const entry of entries) {
          await walkEntry(entry, "", files);
        }
      } catch (err) {
        toast.error(describeError(err, "Failed to read dropped folder."));
        return;
      }
      if (files.length === 0) return;
      const result = await uploadFilesToTarget(target, files);
      if (result.uploaded > 0) {
        toast.success(`Uploaded ${result.uploaded} file${result.uploaded === 1 ? "" : "s"} to ${target}.`);
      }
    };
    container.addEventListener("drop", handler, { capture: true });
    return () => container.removeEventListener("drop", handler, { capture: true });
  }, [rootData, getCurrentPath, uploadFilesToTarget]);

  // URL → svar sync. Fires whenever the URL's ?folder= changes (initial
  // mount, back/forward, paste-and-go) and tells svar to navigate there.
  // For paths deeper than svar's current tree (e.g. a pasted link to
  // /a/b/c when only / is loaded), pre-load each ancestor's listing so
  // set-path can find the target id — svar's set-path is a silent no-op
  // when byId returns null, which happens for folders below an
  // unexpanded lazy parent.
  useEffect(() => {
    if (rootData === null) return;
    const api = apiRef.current;
    if (!api) return;
    const urlFolder = searchParams.get("folder") || "/";
    const state = api.getState();
    const activePanel = (state.activePanel ?? 0) as 0 | 1;
    const currentFolder = state.panels?.[activePanel]?.path ?? "/";
    if (urlFolder === currentFolder) return;

    let cancelled = false;
    (async () => {
      if (urlFolder !== "/") {
        const segments = urlFolder.split("/").filter(Boolean);
        let cur = "/";
        for (let i = 0; i < segments.length - 1; i++) {
          cur = cur === "/" ? `/${segments[i]}` : `${cur}/${segments[i]}`;
          if (cancelled) return;
          const entity = api.getFile(cur);
          if (!entity) return; // ancestor missing — can't continue
          if (entity.lazy) {
            try {
              const listing = await listDataStoreFiles(storeId, cur);
              if (cancelled) return;
              api.exec("provide-data", {
                id: cur,
                data: listingToEntities(listing)
              });
            } catch {
              return; // ancestor unreachable
            }
          }
        }
      }
      if (cancelled) return;
      skipNextSetPathPush.current = true;
      api.exec("set-path", { id: urlFolder });
    })();
    return () => {
      cancelled = true;
    };
  }, [rootData, searchParams, storeId]);

  if (loadError) return <Alert color="red">{loadError}</Alert>;
  if (rootData === null) {
    return (
      <Stack align="center" mt="lg">
        <Loader />
      </Stack>
    );
  }

  // Filemanager wants a real height — without it the inner virtualized list
  // collapses to 0px. Match the right-aside / chat-sidebar layout so the
  // panel fills the page below the header chrome.
  return (
    <Locale words={localeWords} optional>
      <Theme>
        <Box
          ref={containerRef}
          style={{
            height: "calc(100vh - 220px)",
            minHeight: 480,
            border: "1px solid var(--mantine-color-default-border)",
            borderRadius: "var(--mantine-radius-sm)",
            overflow: "hidden"
          }}
        >
          <Filemanager data={rootData} init={init} menuOptions={menuOptionsCallback} />
          {toolbarSlot
            ? createPortal(
                // Mirror svar's own toolbar-button structure (cf. the eye's
                // `wx-preview-icon > wx-button > wxi-eye` markup) so the new
                // icon picks up svar's chrome — padding, hover bg, border
                // radius — for free. The css-module class hash on the
                // wrapping div is build-specific but stable per release.
                // The 22px font-size matches svar's toolbar-icon size (the
                // wxi-eye class sets it via the same hashed class); without
                // it FontAwesome defaults to 14px and the icon looks tiny
                // against the same-sized button background.
                <div className="wx-5PZQQztG wx-preview-icon">
                  <button
                    type="button"
                    className="wx-2ZWgb4 wx-button"
                    title="Upload to current folder"
                    aria-label="Upload to current folder"
                    onClick={() => setUploadTarget(getCurrentPath())}
                  >
                    {/* Arrow-up-from-bracket (rather than file-arrow-up) so
                        the glyph's dark-pixel mass matches svar's outline-
                        style toolbar icons. A solid filled-document icon
                        at 22px reads as significantly heavier against the
                        same-colored button bg, making the button itself
                        look darker even though the bg matches. */}
                    <i
                      className="fa fa-arrow-up-from-bracket"
                      style={{ fontSize: 20, lineHeight: "22px" }}
                    />
                  </button>
                </div>,
                toolbarSlot
              )
            : null}
        </Box>
      </Theme>
      {csvImportTarget ? (
        <CreateSqlDataStoreFromCsvModal
          opened
          onClose={() => setCsvImportTarget(null)}
          sourceStoreId={storeId}
          fileId={csvImportTarget.fileId}
          filename={csvImportTarget.filename}
        />
      ) : null}
      <Modal
        opened={uploadTarget !== null}
        onClose={() => {
          if (!uploadBusy) closeUploadModal();
        }}
        title={`Upload to ${uploadTarget ?? ""}`}
        centered
      >
        <Stack gap="sm">
          <Dropzone
            multiple
            disabled={uploadBusy}
            // Override the default file aggregator so Finder folder drops
            // recurse into subdirectories. The path tagged on each file by
            // walkEntry is what runUpload reads to rebuild the tree under
            // the chosen target.
            getFilesFromEvent={getFilesWithFolderSupport}
            onDrop={(files) => setUploadFiles((prev) => [...prev, ...files])}
            onReject={(rejections) => {
              const first = rejections[0]?.errors?.[0];
              toast.error(first?.message ?? "Some files were rejected.");
            }}
            aria-label="File dropzone"
          >
            <Group justify="center" gap="md" mih={120} style={{ pointerEvents: "none" }}>
              <Dropzone.Idle>
                <i
                  className="fa fa-file-arrow-up"
                  style={{ fontSize: 32, color: "var(--mantine-color-dimmed)" }}
                />
              </Dropzone.Idle>
              <Dropzone.Accept>
                <i className="fa fa-arrow-up-from-bracket" style={{ fontSize: 32 }} />
              </Dropzone.Accept>
              <Dropzone.Reject>
                <i
                  className="fa fa-circle-xmark"
                  style={{ fontSize: 32, color: "var(--mantine-color-red-filled)" }}
                />
              </Dropzone.Reject>
              <div>
                <Text size="sm" fw={500}>
                  Drop files here or click to browse
                </Text>
                <Text size="xs" c="dimmed" mt={4}>
                  Multi-file drops queue up; the upload runs sequentially.
                </Text>
              </div>
            </Group>
          </Dropzone>
          {uploadFiles.length > 0 ? (
            <Stack gap={4}>
              <Text size="xs" c="dimmed">
                Queued ({uploadFiles.length}):
              </Text>
              {uploadFiles.map((f, i) => {
                // Show the relative path when one is set (folder drop) so
                // users can see the directory structure that will be
                // recreated under target. Otherwise just the filename.
                const relPath =
                  (f as { path?: string }).path ?? f.webkitRelativePath ?? "";
                const display = relPath && relPath.includes("/") ? relPath : f.name;
                return (
                  <Text key={`${display}-${i}`} size="xs">
                    {display} ({Math.round(f.size / 1024)} KB)
                  </Text>
                );
              })}
            </Stack>
          ) : null}
          <Group justify="flex-end">
            <Button variant="default" onClick={closeUploadModal} disabled={uploadBusy}>
              Cancel
            </Button>
            <Button
              onClick={runUpload}
              disabled={uploadFiles.length === 0}
              loading={uploadBusy}
            >
              Upload {uploadFiles.length || ""}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Locale>
  );
}

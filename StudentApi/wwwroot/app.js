const FIREBASE_API_KEY = "AIzaSyC2CxzvTaSvbaykOAwJb2UEBI4XgRDZXBg";

(() => {
    "use strict";
    const $ = id => document.getElementById(id);
    const pageSize = 8;
    let token = "", users = [], editingUid = null, currentImage = "";
    let busy = false, loading = false, loadFailed = false, page = 1, pendingDelete = null;
    let toastTimer;

    const icon = name => {
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.classList.add("icon");
        svg.setAttribute("aria-hidden", "true");
        const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
        use.setAttribute("href", `#i-${name}`);
        svg.append(use);
        return svg;
    };
    const initials = value => (value || "Người dùng").trim().split(/\s+/).slice(-2).map(word => word[0]).join("").toUpperCase();
    const hideNotice = element => { element.hidden = true; element.textContent = ""; };

    function message(text, type = "error") {
        const target = $("deleteDialog").open ? $("deleteMessage")
            : $("userDialog").open ? $("formMessage")
            : !$("loginSection").hidden ? $("loginMessage") : $("message");
        target.textContent = text;
        target.dataset.type = type;
        target.hidden = !text;
        if (target === $("message")) {
            clearTimeout(toastTimer);
            if (text) toastTimer = setTimeout(() => hideNotice(target), type === "error" ? 12000 : 6000);
        }
    }

    async function readResponse(response) {
        if (response.status === 204) return null;
        const data = await response.json().catch(() => null);
        if (!response.ok) {
            if (response.status === 401) throw new Error("Phiên đăng nhập đã hết hạn hoặc không hợp lệ. Vui lòng đăng nhập lại.");
            if (response.status === 403) throw new Error("Tài khoản này chưa được cấp quyền quản trị.");
            const code = data?.error?.message || "";
            const errors = {
                INVALID_LOGIN_CREDENTIALS: "Email hoặc mật khẩu chưa đúng. Vui lòng kiểm tra lại.",
                EMAIL_NOT_FOUND: "Không tìm thấy tài khoản với địa chỉ email này.",
                INVALID_PASSWORD: "Mật khẩu chưa đúng. Vui lòng thử lại.",
                USER_DISABLED: "Tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.",
                OPERATION_NOT_ALLOWED: "Phương thức đăng nhập chưa được bật. Vui lòng liên hệ người phụ trách.",
                API_KEY_INVALID: "Cấu hình kết nối chưa hợp lệ. Vui lòng liên hệ người phụ trách.",
                TOO_MANY_ATTEMPTS_TRY_LATER: "Có quá nhiều lần thử đăng nhập. Vui lòng đợi và thử lại."
            };
            const validation = data?.errors ? Object.values(data.errors).flat().join("\n") : null;
            const firebaseMessage = code.startsWith("API key not valid") ? errors.API_KEY_INVALID : errors[code];
            throw new Error(firebaseMessage || data?.message || code || validation || `Không thực hiện được yêu cầu (HTTP ${response.status}).`);
        }
        if (data === null) throw new Error("Phản hồi chưa hợp lệ. Vui lòng tải lại trang và thử lại.");
        return data;
    }

    async function api(path, method = "GET", body) {
        try {
            return await readResponse(await fetch(path, {
                signal: AbortSignal.timeout(30000), method,
                headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
                body: body === undefined ? undefined : JSON.stringify(body)
            }));
        } catch (error) {
            if (error instanceof TypeError) throw new Error("Không kết nối được máy chủ. Vui lòng kiểm tra kết nối và thử lại.");
            throw error;
        }
    }

    async function run(action, button) {
        if (busy) return;
        busy = true;
        const locked = [...document.querySelectorAll("[data-busy-lock]")].map(element => [element, element.disabled]);
        locked.forEach(([element]) => element.disabled = true);
        button?.classList.add("is-loading");
        if (button) button.setAttribute("aria-busy", "true");
        message("");
        try { await action(); }
        catch (error) {
            message(error.name === "TimeoutError" ? "Kết nối quá thời gian chờ. Vui lòng thử lại." : error.message);
        } finally {
            locked.forEach(([element, disabled]) => element.disabled = disabled);
            button?.classList.remove("is-loading");
            button?.removeAttribute("aria-busy");
            busy = false;
        }
    }

    function setPreview(value, label) {
        currentImage = value || "";
        $("preview").hidden = !currentImage;
        $("photoPlaceholder").hidden = !!currentImage;
        if (currentImage) $("preview").src = currentImage;
        else $("preview").removeAttribute("src");
        $("photoLabel").textContent = label || (currentImage ? "Ảnh đại diện hiện tại" : "Chọn ảnh đại diện");
    }

    function resetForm() {
        editingUid = null;
        $("userForm").reset();
        $("password").required = true;
        $("passwordRequired").hidden = false;
        $("password").type = "password";
        const toggle = document.querySelector('[data-password="password"]');
        toggle.setAttribute("aria-pressed", "false");
        toggle.setAttribute("aria-label", "Hiện mật khẩu");
        $("formTitle").textContent = "Thêm người dùng";
        $("formDescription").textContent = "Tạo tài khoản và hoàn thiện hồ sơ người dùng mới.";
        $("passwordHint").textContent = "Mật khẩu dùng để đăng nhập vào tài khoản.";
        $("saveLabel").textContent = "Tạo người dùng";
        hideNotice($("formMessage"));
        setPreview("");
    }

    function openEditor(user) {
        if (busy) return;
        resetForm();
        if (user) {
            editingUid = user.uid;
            $("email").value = user.email || "";
            $("hoTen").value = user.hoten || "";
            $("password").required = false;
            $("passwordRequired").hidden = true;
            $("formTitle").textContent = "Chỉnh sửa người dùng";
            $("formDescription").textContent = "Cập nhật thông tin để hồ sơ luôn đầy đủ và chính xác.";
            $("passwordHint").textContent = "Để trống nếu bạn muốn giữ mật khẩu hiện tại.";
            $("saveLabel").textContent = "Lưu thay đổi";
            setPreview(user.hinhanh);
        }
        $("userDialog").showModal();
        $("hoTen").focus();
    }

    function openDelete(user) {
        if (busy) return;
        pendingDelete = user;
        $("deleteName").textContent = user.hoten || "Người dùng";
        $("deleteEmail").textContent = user.email || "";
        hideNotice($("deleteMessage"));
        $("deleteDialog").showModal();
        $("cancelDelete").focus();
    }

    async function resizePhoto(file) {
        if (!file || file.size > 10 * 1024 * 1024) throw new Error("Vui lòng chọn ảnh không quá 10 MB.");
        let bitmap;
        try { bitmap = await createImageBitmap(file); }
        catch { throw new Error("Không đọc được ảnh. Vui lòng chọn ảnh JPEG hoặc PNG."); }
        try {
            const scale = Math.min(400 / bitmap.width, 400 / bitmap.height, 1);
            const canvas = document.createElement("canvas");
            canvas.width = Math.max(1, Math.round(bitmap.width * scale));
            canvas.height = Math.max(1, Math.round(bitmap.height * scale));
            const context = canvas.getContext("2d");
            context.fillStyle = "#ffffff";
            context.fillRect(0, 0, canvas.width, canvas.height);
            context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
            return canvas.toDataURL("image/jpeg", 0.8);
        } finally { bitmap.close(); }
    }

    function render() {
        const keyword = $("search").value.trim().toLocaleLowerCase("vi");
        const filter = $("photoFilter").value;
        const sort = $("sortOrder").value;
        const filtered = users.filter(user => {
            const text = `${user.hoten || ""} ${user.email || ""}`.toLocaleLowerCase("vi");
            return text.includes(keyword) && (filter === "all" || (filter === "with" ? !!user.hinhanh : !user.hinhanh));
        }).sort((a, b) => {
            const key = sort === "email-asc" ? "email" : "hoten";
            return String(a[key] || "").localeCompare(String(b[key] || ""), "vi", { sensitivity: "base" }) * (sort === "name-desc" ? -1 : 1);
        });
        const pages = Math.max(1, Math.ceil(filtered.length / pageSize));
        page = Math.min(Math.max(1, page), pages);
        const start = (page - 1) * pageSize;
        const withPhoto = users.filter(user => user.hinhanh).length;
        $("totalUsers").textContent = users.length;
        $("usersWithPhoto").textContent = withPhoto;
        $("usersWithoutPhoto").textContent = users.length - withPhoto;
        $("navCount").textContent = users.length;
        $("directoryCount").textContent = users.length;
        $("rows").replaceChildren();
        $("emptyState").hidden = loading || filtered.length > 0;
        $("count").textContent = loading ? "Đang tải hồ sơ..." : loadFailed ? "Chưa tải được dữ liệu" : filtered.length ? `Hiển thị ${start + 1}–${Math.min(start + pageSize, filtered.length)} trong ${filtered.length} người dùng` : "0 người dùng";
        $("pageLabel").textContent = `Trang ${page} / ${pages}`;
        $("previousPage").disabled = loading || page === 1;
        $("nextPage").disabled = loading || page === pages;
        if (loading) {
            const tr = document.createElement("tr"); tr.className = "loading-row";
            const td = document.createElement("td"); td.colSpan = 5; td.textContent = "Đang tải danh sách người dùng..."; tr.append(td); $("rows").append(tr);
            return;
        }
        if (!filtered.length) {
            $("emptyTitle").textContent = loadFailed ? "Chưa tải được danh sách" : users.length ? "Không tìm thấy hồ sơ phù hợp" : "Chưa có người dùng";
            $("emptyDescription").textContent = loadFailed ? "Kiểm tra kết nối và thử tải lại danh sách." : users.length ? "Thử một từ khóa khác hoặc bỏ bộ lọc để xem thêm hồ sơ." : "Thêm người dùng đầu tiên để bắt đầu quản lý hồ sơ.";
            $("emptyAction").textContent = loadFailed ? "Thử tải lại" : users.length ? "Xóa bộ lọc" : "Thêm người dùng đầu tiên";
        }
        filtered.slice(start, start + pageSize).forEach((user, index) => {
            const tr = document.createElement("tr");
            const personCell = document.createElement("td");
            const person = document.createElement("div"); person.className = "person-cell";
            const avatar = document.createElement("span"); avatar.className = "person-avatar"; avatar.dataset.tone = (start + index) % 5; avatar.textContent = initials(user.hoten);
            if (user.hinhanh) {
                const image = document.createElement("img"); image.src = user.hinhanh; image.alt = ""; image.loading = "lazy";
                image.onerror = () => image.remove(); avatar.append(image);
            }
            const name = document.createElement("span"); name.className = "person-name"; name.textContent = user.hoten || "Chưa có tên"; name.title = name.textContent;
            person.append(avatar, name); personCell.append(person); tr.append(personCell);
            const email = document.createElement("td"); email.className = "user-email"; email.textContent = user.email || "—"; email.title = user.email || ""; tr.append(email);
            const uid = document.createElement("td"); uid.className = "user-id"; uid.textContent = user.uid ? `${user.uid.slice(0, 8)}…` : "—"; uid.title = user.uid || ""; tr.append(uid);
            const statusCell = document.createElement("td");
            const status = document.createElement("span"); status.className = `photo-badge${user.hinhanh ? "" : " missing"}`;
            status.append(icon(user.hinhanh ? "check" : "photo"), document.createTextNode(user.hinhanh ? "Đã cập nhật" : "Chưa có ảnh")); statusCell.append(status); tr.append(statusCell);
            const actionsCell = document.createElement("td"); const actions = document.createElement("div"); actions.className = "row-actions";
            for (const [type, label, action] of [["edit", "Chỉnh sửa", () => openEditor(user)], ["trash", "Xóa", () => openDelete(user)]]) {
                const button = document.createElement("button"); button.type = "button"; button.className = `icon-button${type === "trash" ? " delete-action" : ""}`; button.title = `${label} ${user.hoten || user.email}`; button.setAttribute("aria-label", button.title); button.append(icon(type)); button.onclick = action; actions.append(button);
            }
            actionsCell.append(actions); tr.append(actionsCell); $("rows").append(tr);
        });
    }

    async function loadUsers() {
        loading = true; render(); $("reloadButton").classList.add("is-refreshing");
        try {
            const data = await api("/api/users");
            if (!Array.isArray(data)) throw new Error("Danh sách trả về chưa hợp lệ. Vui lòng thử lại.");
            users = data; loadFailed = false;
            $("lastUpdated").textContent = `Cập nhật lúc ${new Date().toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" })} · Thông tin hồ sơ mới nhất`;
        } catch (error) {
            loadFailed = users.length === 0;
            $("lastUpdated").textContent = "Chưa cập nhật được dữ liệu. Vui lòng thử lại.";
            throw error;
        } finally {
            loading = false; render(); $("reloadButton").classList.remove("is-refreshing");
        }
    }

    $("loginForm").onsubmit = event => {
        event.preventDefault();
        run(async () => {
            message("Đang kiểm tra tài khoản...", "info");
            let response;
            try {
                response = await fetch(`https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=${FIREBASE_API_KEY}`, {
                    method: "POST", signal: AbortSignal.timeout(30000), headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ email: $("loginEmail").value.trim(), password: $("loginPassword").value, returnSecureToken: true })
                });
            } catch (error) {
                if (error.name === "TimeoutError") throw error;
                throw new Error("Không kết nối được dịch vụ đăng nhập. Vui lòng kiểm tra Internet.");
            }
            const result = await readResponse(response);
            if (!result?.idToken) throw new Error("Không tạo được phiên đăng nhập. Vui lòng thử lại.");
            token = result.idToken;
            try { await api("/api/users/check-admin"); }
            catch (error) { token = ""; throw error; }
            $("accountEmail").textContent = $("loginEmail").value.trim();
            $("accountEmail").title = $("accountEmail").textContent;
            $("accountAvatar").textContent = initials($("loginEmail").value.split("@")[0].replace(/[._]/g, " "));
            $("loginPassword").value = "";
            hideNotice($("loginMessage"));
            $("loginSection").hidden = true; $("adminSection").hidden = false;
            document.title = "Người dùng — Nội Bộ";
            try { await loadUsers(); }
            catch (error) { message(`Đã đăng nhập, nhưng chưa tải được danh sách. ${error.message}`, "warning"); }
        }, $("loginButton"));
    };

    $("userForm").onsubmit = event => {
        event.preventDefault();
        run(async () => {
            if (!currentImage) { $("photo").focus(); throw new Error("Vui lòng chọn ảnh đại diện cho người dùng."); }
            const payload = { email: $("email").value.trim(), matkhau: $("password").value || null, hoten: $("hoTen").value.trim(), hinhanh: currentImage };
            if (!payload.hoten) throw new Error("Vui lòng nhập họ tên người dùng.");
            const editing = !!editingUid;
            await api(editing ? `/api/users/${encodeURIComponent(editingUid)}` : "/api/users", editing ? "PUT" : "POST", payload);
            $("userDialog").close(); resetForm();
            try { await loadUsers(); message(editing ? "Đã lưu thay đổi hồ sơ." : "Đã tạo người dùng mới.", "success"); }
            catch (error) { message(`Đã lưu người dùng, nhưng chưa tải lại được danh sách. ${error.message}`, "warning"); }
        }, $("saveButton"));
    };

    $("photo").onchange = () => {
        const file = $("photo").files[0];
        if (!file) return;
        run(async () => setPreview(await resizePhoto(file), file.name), $("saveButton"));
    };
    $("confirmDelete").onclick = () => {
        if (!pendingDelete) return;
        run(async () => {
            await api(`/api/users/${encodeURIComponent(pendingDelete.uid)}`, "DELETE");
            users = users.filter(user => user.uid !== pendingDelete.uid);
            pendingDelete = null; $("deleteDialog").close(); render();
            message("Đã xóa người dùng.", "success");
        }, $("confirmDelete"));
    };
    $("addButton").onclick = () => openEditor();
    $("emptyAction").onclick = () => {
        if (loadFailed) { run(loadUsers, $("reloadButton")); return; }
        if (users.length) { $("search").value = ""; $("photoFilter").value = "all"; page = 1; render(); }
        else openEditor();
    };
    for (const id of ["closeUserDialog", "cancelButton"]) $(id).onclick = () => { if (!busy) $("userDialog").close(); };
    $("cancelDelete").onclick = () => { if (!busy) { $("deleteDialog").close(); pendingDelete = null; } };
    for (const id of ["userDialog", "deleteDialog"]) {
        $(id).addEventListener("cancel", event => { if (busy) event.preventDefault(); });
        $(id).addEventListener("click", event => {
            if (!busy && event.target === $(id)) {
                const bounds = $(id).getBoundingClientRect();
                if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) $(id).close();
            }
        });
    }
    $("search").oninput = () => { page = 1; render(); };
    for (const id of ["photoFilter", "sortOrder"]) $(id).onchange = () => { page = 1; render(); };
    $("previousPage").onclick = () => { if (page > 1) { page--; render(); } };
    $("nextPage").onclick = () => { page++; render(); };
    $("reloadButton").onclick = () => run(loadUsers, $("reloadButton"));
    $("logoutButton").onclick = () => {
        if (busy) return;
        token = ""; users = []; page = 1; pendingDelete = null; loadFailed = false;
        $("userDialog").close(); $("deleteDialog").close(); resetForm();
        $("loginForm").reset(); $("loginPassword").type = "password";
        const toggle = document.querySelector('[data-password="loginPassword"]');
        toggle.setAttribute("aria-pressed", "false"); toggle.setAttribute("aria-label", "Hiện mật khẩu");
        $("accountEmail").textContent = ""; $("accountAvatar").textContent = "QT";
        $("search").value = ""; $("photoFilter").value = "all"; $("sortOrder").value = "name-asc";
        hideNotice($("message")); hideNotice($("loginMessage"));
        $("adminSection").hidden = true; $("loginSection").hidden = false;
        document.title = "Nội Bộ — Quản lý người dùng"; $("loginEmail").focus();
    };
    document.querySelectorAll("[data-password]").forEach(button => {
        button.onclick = () => {
            const input = $(button.dataset.password);
            input.type = input.type === "password" ? "text" : "password";
            button.setAttribute("aria-pressed", String(input.type === "text"));
            button.setAttribute("aria-label", input.type === "text" ? "Ẩn mật khẩu" : "Hiện mật khẩu");
        };
    });
    document.querySelectorAll("[data-help]").forEach(button => button.onclick = () => $("helpDialog").showModal());
    $("closeHelp").onclick = () => $("helpDialog").close();
    render();
})();

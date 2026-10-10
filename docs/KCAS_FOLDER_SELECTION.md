# KCAS Client Folder Selection

The KCAS server-folder browser is the only client-folder selection method.
The experimental browser reconstruction and native-extension routes were removed
after they did not work satisfactorily on the user's workstation.

On Client compliance review, Change folder opens ServerFolderPicker directly.
It starts at the saved client folder when that location exists on the server,
otherwise at the active configured client-folder root when one is available.

Open a directory to navigate into it, use Parent to move up, and choose Select
beside a directory or Select this folder for the current location. Cancel changes
nothing. The authorised save service checks that the selected directory exists,
saves its complete server path, and records the saving user and audit.

Only directory navigation occurs. Saving does not scan client evidence, upload
files, change linked documents or rerun assessments. The client page refreshes
and confirms the saved folder after a successful save.

Live folder selection uses locations accessible to the server, such as E:, not
the workstation's Z: mapping. Workstation path display continues to use the
existing DocumentPathDisplayService configuration.

No browser extension, native Windows helper, browser folder permission or
IndexedDB folder handle is required. The previously installed experimental
native host is not called by KCAS; this change does not alter workstation policies
or remove files outside the repository.

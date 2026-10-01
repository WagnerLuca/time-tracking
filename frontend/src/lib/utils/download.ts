/** Save an API response body (requested with `responseType: 'blob'`) as a file. */
export function downloadBlob(data: BlobPart, type: string, filename: string) {
	const url = URL.createObjectURL(new Blob([data], { type }));
	const a = document.createElement('a');
	a.href = url;
	a.download = filename;
	a.click();
	URL.revokeObjectURL(url);
}

export const base_url :string = (window.location.port === "4200") ? "http://127.0.0.1:7032" : "";
export const image_actions  :string = "/api/BMP";
export const sound_actions  :string = "/api/WAV";
export const model_actions  :string = "/api/GLB";
export const export_actions : string="/api/Export";

export function force_reload(){

    sessionStorage.clear();
    window.location.reload();

}
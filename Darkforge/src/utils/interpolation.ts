export function get_key_range(former:number,next:number):number{

    while(next<=former){
        next+=1.0;
    }

    return next - former;

}

export function get_interpolation_value(current:number,delta:number,former:number,next:number):number{

    return current + (delta / get_key_range(former,next));

}

export function advance_track(frames:number[],index:number,interp:number,delta:number):[number,number]{

    const length = frames.length;

    if(length===0){
        return [index,0];
    }

    let range = get_key_range(frames[index%length],frames[(index+1)%length]);

    interp += delta / range;

    let guard = 0;

    while(interp>=1.0 && guard<length){

        const leftover = (interp - 1.0) * range;

        index++;

        range = get_key_range(frames[index%length],frames[(index+1)%length]);

        interp = leftover / range;

        guard++;
    }

    return [index,Math.min(Math.max(interp,0.0),1.0)];

}